using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using DeltaQ.RTB.Storage;
using DeltaQ.RTB.Utility;

using ITimer = DeltaQ.RTB.Utility.ITimer;

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
		ICacheActionLog _cacheActionLog;
		IRemoteStorage _remoteStorage;

		object _sync = new object();
		Dictionary<string, FileState> _cache = new Dictionary<string, FileState>();
		List<FileState> _currentBatch = new List<FileState>();
		int _currentBatchNumber;
		StreamWriter? _currentBatchWriter;
		ITimerInstance? _batchUploadTimer;
		object _consolidationSync = new object();

		volatile bool _stopping;
		volatile int _busyCount;
		object _busySync = new object();

		internal const long DeletedFileSize = -1;
		internal const string DeletedChecksum = "-";

		class BusyScope : IDisposable
		{
			RemoteFileStateCache? _owner;

			public BusyScope(RemoteFileStateCache owner)
			{
				_owner = owner;
				_owner._busyCount++;
			}

			public void Dispose()
			{
				if (_owner != null)
				{
					_owner._busyCount--;
					_owner = null;
				}
			}
		}

		IDisposable Busy() => new BusyScope(this);

		public void Start()
		{
			_cacheActionLog.EnsureDirectoryExists();

			List<long> outstandingActionKeys = new List<long>(_cacheActionLog.EnumerateActionKeys());

			outstandingActionKeys.Sort();

			lock (_actionThreadSync)
			{
				_actionQueue.Clear();

				foreach (var key in outstandingActionKeys)
				{
					string path = _cacheActionLog.GetQueueActionFileName(key);

					try
					{
						string serialized = File.ReadAllText(path);

						var action = CacheAction.Deserialize(serialized);

						_actionQueue.Enqueue(action);
					}
					catch (Exception e)
					{
						Console.Error.WriteLine("Possible consistency problem. Error rehydrating Remote File State Cache action from path: " + _cacheActionLog.ActionQueuePath);
						Console.Error.WriteLine("=> {0}: {1}", e.GetType().Name, e.Message);
					}
				}
			}

			StartActionThread();
		}

		public void Stop()
		{
			_stopping = true;
			WakeActionThread();
		}

		public void WaitWhileBusy()
		{
			lock (_busySync)
			{
				while (_busyCount > 0)
					Monitor.Wait(_busySync);
			}
		}

		internal Dictionary<string, FileState> GetCacheForTest() => _cache;
		internal int GetCurrentBatchNumberForTest() => _currentBatchNumber;
		internal List<FileState> GetCurrentBatchForTest() => _currentBatch;

		public RemoteFileStateCache(OperatingParameters parameters, ITimer timer, IRemoteFileStateCacheStorage cacheStorage, ICacheActionLog cacheActionLog, IRemoteStorage remoteStorage)
		{
			_parameters = parameters;

			_timer = timer;
			_cacheStorage = cacheStorage;
			_cacheActionLog = cacheActionLog;
			_remoteStorage = remoteStorage;

			LoadCache();
		}

		public bool ContainsPath(string path)
		{
			lock (_sync)
				return _cache.ContainsKey(path);
		}

		public IEnumerable<string> EnumeratePaths()
		{
			lock (_sync)
				return _cache.Keys.ToList();
		}

		public FileState? GetFileState(string path)
		{
			lock (_sync)
			{
				_cache.TryGetValue(path, out var state);

				return state;
			}
		}

		public void UpdateFileState(string path, FileState newFileState)
		{
			lock (_sync)
			{
				_cache[path] = newFileState;

				newFileState.Path = path; // Just in case.

				AppendNewFileStateToCurrentBatch(newFileState);
			}
		}

		public bool RemoveFileState(string path)
		{
			lock (_sync)
			{
				if (_cache.TryGetValue(path, out var fileState))
				{
					_cache.Remove(path);

					AppendNewFileStateToCurrentBatch(
						new FileState()
						{
							Path = fileState.Path,
							FileSize = DeletedFileSize,
							Checksum = DeletedChecksum,
						});

					return true;
				}

				return false;
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

						if (fileState.FileSize == DeletedFileSize)
							_cache.Remove(fileState.Path);
						else
						{
							// Overwrite if already present, as the one we just loaded will be newer.
							_cache[fileState.Path] = fileState;
						}
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
			lock (_sync)
			{
				_currentBatch.Add(newFileState);

				if (_batchUploadTimer == null)
					_batchUploadTimer = _timer.ScheduleAction(_parameters.BatchUploadConsolidationDelay, BatchUploadTimerElapsed);

				if (_currentBatchWriter == null)
				{
					if (_parameters.Verbose)
						Console.WriteLine("[RFSC] Opening batch writer for batch number {0}", _currentBatchNumber);

					_currentBatchWriter = _cacheStorage.OpenBatchFileWriter(_currentBatchNumber);
					_currentBatchWriter.AutoFlush = true;
				}

				_currentBatchWriter.WriteLine(newFileState);
			}
		}

		void BatchUploadTimerElapsed()
		{
			lock (_sync)
			{
				_batchUploadTimer?.Dispose();
				_batchUploadTimer = null;
			}

			if (!_stopping)
				using (Busy())
					UploadCurrentBatchAndBeginNext();
		}

		public void UploadCurrentBatchAndBeginNext(bool deferConsolidation = false)
		{
			int batchNumberToUpload = -1;

			lock (_sync)
			{
				if (_currentBatch.Any())
				{
					batchNumberToUpload = _currentBatchNumber;

					_currentBatchNumber++;
					_currentBatch.Clear();
					_currentBatchWriter?.Close();
					_currentBatchWriter = null;
				}
			}

			if (batchNumberToUpload > 0)
				UploadBatch(batchNumberToUpload);

			bool shouldConsolidate = false;

			lock (this)
			{
				if (_cacheStorage.EnumerateBatches().Count() > 3)
					shouldConsolidate = true;
			}

			if (shouldConsolidate && !deferConsolidation)
			{
				bool consolidated;

				do
				{
					consolidated = false;

					lock (_consolidationSync)
					{
						lock (this)
						{
							if (_cacheStorage.EnumerateBatches().Count() <= 3)
								shouldConsolidate = false;
						}

						if (shouldConsolidate)
						{
							int removedBatchNumber = ConsolidateOldestBatch();

							if (removedBatchNumber >= 0)
							{
								consolidated = true;

								string remoteBatchPath = "/state/" + removedBatchNumber;

								QueueAction(CacheAction.DeleteFile(remoteBatchPath));
							}
						}
					}
				}
				while (consolidated);
			}
		}

		internal void UploadBatch(int batchNumberToUpload)
		{
			string batchRemotePath = "/state/" + batchNumberToUpload;

			var temporaryCopyPath = Path.GetTempFileName();

			using (var temporaryCopy = File.Open(temporaryCopyPath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
			{
				temporaryCopy.SetLength(0);

				using (var stream = _cacheStorage.OpenBatchFileStream(batchNumberToUpload))
					stream.CopyTo(temporaryCopy);

				QueueAction(CacheAction.UploadFile(temporaryCopyPath, batchRemotePath));
			}
		}

		internal int ConsolidateOldestBatch()
		{
			// Find the two oldest batches.
			// If there is only one batch, nothing to do.
			var batchNumbers = new List<int>(_cacheStorage.EnumerateBatches());

			if (batchNumbers.Count < 2)
				return -1;

			batchNumbers.Sort();

			var oldestBatchNumber = batchNumbers[0];
			var mergeIntoBatchNumber = batchNumbers[1];

			// Load in the merge-into batch. If it contains any deletions of file states from the previous batch, we
			// need to make sure we don't merge those deleted entries in. But, we don't need to keep them because
			// after the consolidation, there won't be any older state that needs to be deleted in the first place.
			var mergeIntoBatch = new Dictionary<string, FileState>();

			var deletedPaths = new HashSet<string>();

			using (var reader = _cacheStorage.OpenBatchFileReader(mergeIntoBatchNumber))
			{
				while (true)
				{
					string? line = reader.ReadLine();

					if (line == null)
						break;

					var fileState = FileState.Parse(line);

					if (fileState.FileSize == DeletedFileSize)
						deletedPaths.Add(fileState.Path);
					else
						mergeIntoBatch[fileState.Path] = fileState;
				}
			}

			// Merge the oldest batch into the merge-into batch. Only entries that aren't superseded in the merge-into
			// batch are used. When the merge-into branch already has a newer FileState, the older one is discarded.
			// If we encounter a path that the merge-into branch had a deletion for, we don't want to merge it in --
			// otherwise we'd be effectively undoing the deletion.
			using (var reader = _cacheStorage.OpenBatchFileReader(oldestBatchNumber))
			{
				while (true)
				{
					string? line = reader.ReadLine();

					if (line == null)
						break;

					var fileState = FileState.Parse(line);

					if (!deletedPaths.Contains(fileState.Path)
					 && !mergeIntoBatch.ContainsKey(fileState.Path))
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

			UploadBatch(mergeIntoBatchNumber);

			// Return the batch number that no longer exists so that the caller can remove it from remote storage.
			return oldestBatchNumber;
		}

		void StartActionThread()
		{
			Thread thread = new Thread(ActionThreadProc);

			thread.Name = "Remote File State Cache Action Thread";
			thread.Start();
		}

		void WakeActionThread()
		{
			lock (_actionThreadSync)
				Monitor.PulseAll(_actionThreadSync);
		}

		void QueueAction(CacheAction action)
		{
			_cacheActionLog.LogAction(action);

			lock (_actionThreadSync)
			{
				_actionQueue.Enqueue(action);
				WakeActionThread();
			}
		}

		internal void DrainActionQueue()
		{
			lock (_actionThreadSync)
				while (_actionQueue.Count > 0)
					Monitor.Wait(_actionThreadSync, TimeSpan.FromSeconds(5));
		}

		object _actionThreadSync = new object();
		Queue<CacheAction> _actionQueue = new Queue<CacheAction>();

		void ActionThreadProc()
		{
			while (!_stopping)
			{
				CacheAction action;

				lock (_actionThreadSync)
				{
					if (_actionQueue.Count == 0)
					{
						Monitor.Wait(_actionThreadSync);
						continue;
					}

					action = _actionQueue.Dequeue();

					while (!action.IsComplete)
						ProcessCacheAction(action);

					try
					{
						if (action.ActionFileName != null)
						{
							File.Delete(action.ActionFileName);
							action.ActionFileName = null;
						}
					}
					catch {}

					Monitor.PulseAll(_actionThreadSync);
				}
			}
		}

		void ProcessCacheAction(CacheAction action)
		{
			try
			{
				switch (action.CacheActionType)
				{
					case CacheActionType.UploadFile:
						using (var stream = File.OpenRead(action.SourcePath!))
							_remoteStorage.UploadFileDirect(action.Path!, stream, CancellationToken.None);
						File.Delete(action.SourcePath!);
						break;
					case CacheActionType.DeleteFile:
						_remoteStorage.DeleteFile(action.Path!, CancellationToken.None);
						break;
				}

				action.IsComplete = true;
			}
			catch
			{
				Thread.Sleep(TimeSpan.FromSeconds(5));
			}
		}
	}
}

