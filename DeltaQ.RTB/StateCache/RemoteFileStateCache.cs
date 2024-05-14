using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using DeltaQ.RTB.Storage;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.StateCache
{
	public class RemoteFileStateCache : IRemoteFileStateCache
	{
		// Objectives:
		// 1. Maintain a look-up based on file full paths that report back the file size and MD5 checksum of each file stored on the server
		// 2. Track updates in a sequence of batch files, where the current state is the result of overlaying the batches in order of date
		// 3. Be able to reload the current state from the batch files at any time (e.g. after reboots)
		// 3. Keep batch files synchronized to the server as well
		// 4. Have a mechanism to consolidate batches, merging the oldest batch forward, and updating the server's copy in the same way
		//    => Merge algorithm: if we call the oldest batch 0 and its successor 1, then add all files from 0 that aren't in 1 to 1, and then
		//                        discard 0.
		OperatingParameters _parameters;

		ITimer _timer;
		IRemoteFileStateCacheStorage _cacheStorage;
		IRemoteStorage _remoteStorage;
		Dictionary<string, FileState> _cache = new Dictionary<string, FileState>();
		List<FileState> _currentBatch = new List<FileState>();
		int _currentBatchNumber;
		StreamWriter? _currentBatchWriter;
		ITimerInstance? _batchUploadTimer;

		internal Dictionary<string, FileState> GetCacheForTest() => _cache;
		internal int GetCurrentBatchNumberForTest() => _currentBatchNumber;

		public RemoteFileStateCache(OperatingParameters parameters, ITimer timer, IRemoteFileStateCacheStorage cacheStorage, IRemoteStorage remoteStorage)
		{
			_parameters = parameters;

			_timer = timer;
			_cacheStorage = cacheStorage;
			_remoteStorage = remoteStorage;

			LoadCache();
		}

		public FileState? GetFileState(string path)
		{
			_cache.TryGetValue(path, out var state);

			return state;
		}

		public void UpdateFileState(string path, FileState newFileState)
		{
			lock (this)
			{
				_cache[path] = newFileState;

				newFileState.Path = path; // Just in case.

				AppendNewFileStateToCurrentBatch(newFileState);
			}
		}

		void LoadCache()
		{
			// Enumerate the batch numbers that are stored locally. Process them in order.
			var batchNumbers = new List<int>(_cacheStorage.EnumerateBatches());

			batchNumbers.Sort();

			// Load in all saved FileStates. If we encounter one that we already have in the cache,
			// it is a newer state that supersedes the previously-loaded one.
			foreach (var batchNumber in batchNumbers)
			{
				using (var reader = _cacheStorage.OpenBatchFileReader(batchNumber))
				{
					while (true)
					{
						var line = reader.ReadLine();

						if (line == null)
							break;

						var fileState = FileState.Parse(line);

						// Overwrite if already present, as the one we just loaded will be newer.
						_cache[fileState.Path] = fileState;
					}
				}
			}

			if (batchNumbers.Any())
				_currentBatchNumber = batchNumbers.Max() + 1;
			else
				_currentBatchNumber = 1;
		}

		internal void AppendNewFileStateToCurrentBatch(FileState newFileState)
		{
			lock (this)
			{
				_currentBatch.Add(newFileState);

				if (_batchUploadTimer == null)
					_batchUploadTimer = _timer.ScheduleAction(_parameters.BatchUploadConsolidationDelay, BatchUploadTimerElapsed);

				if (_currentBatchWriter == null)
				{
					_currentBatchWriter = _cacheStorage.OpenBatchFileWriter(_currentBatchNumber);
					_currentBatchWriter.AutoFlush = true;
				}

				_currentBatchWriter.WriteLine(newFileState);
			}
		}

		void BatchUploadTimerElapsed()
		{
			lock (this)
			{
				_batchUploadTimer?.Dispose();
				_batchUploadTimer = null;

				UploadCurrentBatch();
			}
		}

		internal void UploadCurrentBatch()
		{
			lock (this)
			{
				int batchNumberToUpload = _currentBatchNumber;

				string currentBatchRemotePath = "/state/" + _currentBatchNumber;

				_currentBatchNumber++;
				_currentBatch.Clear();
				_currentBatchWriter?.Close();
				_currentBatchWriter = null;

				using (var stream = _cacheStorage.OpenBatchFileStream(batchNumberToUpload))
					_remoteStorage.UploadFile(currentBatchRemotePath, stream);

				if (_cacheStorage.EnumerateBatches().Count() > 3)
				{
					int removedBatchNumber = ConsolidateOldestBatch();

					if (removedBatchNumber >= 0)
					{
						string remoteBatchPath = "/state/" + removedBatchNumber;

						_remoteStorage.DeleteFile(remoteBatchPath);
					}
				}
			}
		}

		internal int ConsolidateOldestBatch()
		{
			lock (this)
			{
				// Find the two oldest batches.
				// If there is only one batch, nothing to do.
				var batchNumbers = new List<int>(_cacheStorage.EnumerateBatches());

				if (batchNumbers.Count < 2)
					return -1;

				batchNumbers.Sort();

				var oldestBatchNumber = batchNumbers[0];
				var mergeIntoBatchNumber = batchNumbers[1];

				// Load in the merge-into batch.
				var mergeIntoBatch = new Dictionary<string, FileState>();

				using (var reader = _cacheStorage.OpenBatchFileReader(mergeIntoBatchNumber))
				{
					while (true)
					{
						string? line = reader.ReadLine();

						if (line == null)
							break;

						var fileState = FileState.Parse(line);

						mergeIntoBatch[fileState.Path] = fileState;
					}
				}

				// Merge the oldest batch into the merge-into batch. Only entries that aren't superseded in the merge-into
				// batch are used. When the merge-into branch already has a newer FileState, the older one is discarded.
				using (var reader = _cacheStorage.OpenBatchFileReader(oldestBatchNumber))
				{
					while (true)
					{
						string? line = reader.ReadLine();

						if (line == null)
							break;

						var fileState = FileState.Parse(line);

						if (!mergeIntoBatch.ContainsKey(fileState.Path))
							mergeIntoBatch[fileState.Path] = fileState;
					}
				}

				// Write out the merged batch. It is written to a ".new" file first, so that the update to the actual
				// batch file path is atomic and cannot possibly contain an incomplete file in the event of an error
				// or power loss or whatnot.
				using (var writer = _cacheStorage.OpenNewBatchFileWriter(mergeIntoBatchNumber))
					foreach (var fileState in mergeIntoBatch.Values)
						writer.WriteLine(fileState);

				_cacheStorage.SwitchToConsolidatedFile(
					oldestBatchNumber,
					mergeIntoBatchNumber);

				// Return the batch number that no longer exists so that the caller can remove it from remote storage.
				return oldestBatchNumber;
			}
		}
	}
}

