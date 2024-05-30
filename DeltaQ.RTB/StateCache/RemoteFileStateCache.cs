using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using DeltaQ.RTB.Diagnostics;
using DeltaQ.RTB.Storage;
using DeltaQ.RTB.Utility;

using ITimer = DeltaQ.RTB.Utility.ITimer;

namespace DeltaQ.RTB.StateCache
{
	public class RemoteFileStateCache : DiagnosticOutputBase, IRemoteFileStateCache
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

		IErrorLogger _errorLogger;
		ITimer _timer;
		IRemoteFileStateCacheStorage _cacheStorage;
		ICacheActionLog _cacheActionLog;
		IRemoteStorage _remoteStorage;

		object _sync = new object();
		Dictionary<string, FileState> _cache = new Dictionary<string, FileState>();
		bool _cacheLoaded = false;
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

		void DebugLog(string line)
		{
			if (_parameters.RemoteFileStateCacheDebugLogPath != null)
			{
				lock (this)
					using (var writer = new StreamWriter(_parameters.RemoteFileStateCacheDebugLogPath, append: true))
						writer.WriteLine("[{0}] {1}", Thread.CurrentThread.ManagedThreadId, line);
			}
		}

		void DebugLog(object? value)
		{
			if (_parameters.RemoteFileStateCacheDebugLogPath != null)
				DebugLog(value?.ToString() ?? "");
		}

		void DebugLog(string format, params object?[] args)
		{
			DebugLog(string.Format(format, args));
		}

		class BusyScope : IDisposable
		{
			RemoteFileStateCache? _owner;

			public BusyScope(RemoteFileStateCache owner)
			{
				_owner = owner;

				var newCount = Interlocked.Increment(ref _owner._busyCount);

				_owner.DebugLog("[BUSY] count is now {0}", newCount);
			}

			public void Dispose()
			{
				if (_owner != null)
				{
					var newCount = Interlocked.Decrement(ref _owner._busyCount);

					_owner.DebugLog("[BUSY] count is now {0}", newCount);

					lock (_owner._busySync)
						Monitor.PulseAll(_owner._busySync);

					_owner = null;
				}
			}
		}

		IDisposable Busy() => new BusyScope(this);

		public void Start()
		{
			EnsureCacheIsLoaded();

			DebugLog("starting");

			_cacheActionLog.EnsureDirectoryExists();

			List<long> outstandingActionKeys = new List<long>(_cacheActionLog.EnumerateActionKeys());

			DebugLog("enumerated existing action keys, found {0} of them", outstandingActionKeys.Count);

			outstandingActionKeys.Sort();

			lock (_actionThreadSync)
			{
				_actionQueue.Clear();

				foreach (var key in outstandingActionKeys)
				{
					try
					{
						var action = _cacheActionLog.RehydrateAction(key);

						DebugLog("enqueuing action");

						_actionQueue.Enqueue(action);
					}
					catch (Exception exception)
					{
						_errorLogger.LogError(
							$"An error occurred rehydrating an Remote File State Cache action with key {key} from path:\n" +
							"\n" +
							_cacheActionLog.ActionQueuePath + "\n" +
							"\n" +
							"This is a possible consistency problem.",
							exception);
					}
				}

				DebugLog("enqueued {0} actions", _actionQueue.Count);
			}

			DebugLog("starting action thread");

			StartActionThread();
		}

		public void Stop()
		{
			DebugLog("stopping");

			_stopping = true;
			WakeActionThread();
		}

		public void WaitWhileBusy()
		{
			DebugLog("WaitWhileBusy: about to lock busysync");

			lock (_busySync)
			{
				while (_busyCount > 0)
				{
					DebugLog("WaitWhileBusy: busy count is {0}, waiting", _busyCount);
					Monitor.Wait(_busySync);
				}

				DebugLog("WaitWhileBusy: not busy");
			}
		}

		internal Dictionary<string, FileState> GetCacheForTest() => _cache;
		internal int GetCurrentBatchNumberForTest() => _currentBatchNumber;
		internal List<FileState> GetCurrentBatchForTest() => _currentBatch;

		public RemoteFileStateCache(OperatingParameters parameters, IErrorLogger errorLogger, ITimer timer, IRemoteFileStateCacheStorage cacheStorage, ICacheActionLog cacheActionLog, IRemoteStorage remoteStorage)
		{
			_parameters = parameters;

			_errorLogger = errorLogger;
			_timer = timer;
			_cacheStorage = cacheStorage;
			_cacheActionLog = cacheActionLog;
			_remoteStorage = remoteStorage;
		}

		public bool ContainsPath(string path)
		{
			lock (_sync)
			{
				EnsureCacheIsLoaded();

				return _cache.ContainsKey(path);
			}
		}

		public IEnumerable<string> EnumeratePaths()
		{
			lock (_sync)
			{
				EnsureCacheIsLoaded();

				return _cache.Keys.ToList();
			}
		}

		public IEnumerable<FileState> EnumerateFileStates()
		{
			lock (_sync)
			{
				EnsureCacheIsLoaded();

				return _cache.Values.ToList();
			}
		}

		public FileState? GetFileState(string path)
		{
			lock (_sync)
			{
				EnsureCacheIsLoaded();

				_cache.TryGetValue(path, out var state);

				return state;
			}
		}

		public void UpdateFileState(string path, FileState newFileState)
		{
			lock (_sync)
			{
				EnsureCacheIsLoaded();

				DebugLog("updating file state for: {0}", path);

				_cache[path] = newFileState;

				newFileState.Path = path; // Just in case.

				AppendNewFileStateToCurrentBatch(newFileState);
			}
		}

		public bool RemoveFileState(string path)
		{
			lock (_sync)
			{
				EnsureCacheIsLoaded();

				DebugLog("removing file state for: {0}", path);

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

		void EnsureCacheIsLoaded()
		{
			if (!_cacheLoaded)
				LoadCache();
		}

		public void LoadCache()
		{
			DebugLog("loading cache");

			// Enumerate the batch numbers that are stored locally. Process them in order.
			var batchNumbers = new List<int>(_cacheStorage.EnumerateBatches());

			batchNumbers.Sort();

			DebugLog("found {0} batches", batchNumbers.Count);

			// Load in all saved FileStates. If we encounter one that we already have in the cache,
			// it is a newer state that supersedes the previously-loaded one.
			foreach (var batchNumber in batchNumbers)
			{
				DebugLog("applying batch {0}", batchNumber);

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

			DebugLog("current batch number is: {0}", _currentBatchNumber);

			_cacheLoaded = true;
		}

		internal void AppendNewFileStateToCurrentBatch(FileState newFileState)
		{
			lock (_sync)
			{
				DebugLog("appending new file state to current batch");

				_currentBatch.Add(newFileState);

				if (_batchUploadTimer == null)
				{
					DebugLog("scheduling batch upload");
					_batchUploadTimer = _timer.ScheduleAction(_parameters.BatchUploadConsolidationDelay, BatchUploadTimerElapsed);
				}

				if (_currentBatchWriter == null)
				{
					DebugLog("opening batch writer for batch {0}", _currentBatchNumber);

					VerboseDiagnosticOutput("[RFSC] Opening batch writer for batch number {0}", _currentBatchNumber);

					_currentBatchWriter = _cacheStorage.OpenBatchFileWriter(_currentBatchNumber);
					_currentBatchWriter.AutoFlush = true;
				}

				DebugLog("writing state to current batch writer");

				_currentBatchWriter.WriteLine(newFileState);
			}
		}

		void BatchUploadTimerElapsed()
		{
			DebugLog("batch upload timer elapsed");

			lock (_sync)
			{
				DebugLog("disposing of batch upload timer");

				_batchUploadTimer?.Dispose();
				_batchUploadTimer = null;
			}

			if (!_stopping)
			{
				DebugLog("about to call UploadCurrentBatchAndBeginNext");

				using (Busy())
					UploadCurrentBatchAndBeginNext();
			}
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

			DebugLog("batch number to upload is {0}", batchNumberToUpload);

			if (batchNumberToUpload > 0)
				UploadBatch(batchNumberToUpload);

			DebugLog("checking if should consolidate");

			bool shouldConsolidate = false;

			lock (_sync)
			{
				int count = _cacheStorage.EnumerateBatches().Count();

				DebugLog("=> enumerate batches found {0} batches", count);

				if (_cacheStorage.EnumerateBatches().Count() > 3)
					shouldConsolidate = true;
			}

			DebugLog("should consolidate: {0}", shouldConsolidate);
			DebugLog("defer consolidation? {0}", deferConsolidation);

			if (shouldConsolidate && !deferConsolidation)
			{
				bool consolidated;

				// Consolidate at most 2 batches per call
				for (int i=0; i < 2; i++)
				{
					consolidated = false;

					lock (_consolidationSync)
					{
						lock (_sync)
						{
							int count = _cacheStorage.EnumerateBatches().Count();

							if (count <= 3)
							{
								DebugLog("actually cancelling consolidation because after synchronizing, there are only {0} batches", count);
								shouldConsolidate = false;
							}
						}

						if (shouldConsolidate)
						{
							int removedBatchNumber = ConsolidateOldestBatch();

							DebugLog("ConsolidateOldestBatch says it removed batch number {0}", removedBatchNumber);

							if (removedBatchNumber >= 0)
							{
								consolidated = true;

								string remoteBatchPath = "/state/" + removedBatchNumber;

								DebugLog("=> queuing deletion of {0}", remoteBatchPath);

								QueueAction(CacheAction.DeleteFile(remoteBatchPath));
							}
						}
					}

					if (!consolidated)
						break;
				}
			}
		}

		internal void UploadBatch(int batchNumberToUpload)
		{
			DebugLog("beginning UploadBatch of {0}", batchNumberToUpload);

			string batchRemotePath = "/state/" + batchNumberToUpload;

			var temporaryCopyPath = Path.GetTempFileName();

			using (var temporaryCopy = File.Open(temporaryCopyPath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
			{
				DebugLog("=> making copy to {0}", temporaryCopyPath);

				temporaryCopy.SetLength(0);

				using (var stream = _cacheStorage.OpenBatchFileStream(batchNumberToUpload))
					stream.CopyTo(temporaryCopy);

				DebugLog("=> queuing action: upload {0} to {1}", temporaryCopyPath, batchRemotePath);

				QueueAction(CacheAction.UploadFile(temporaryCopyPath, batchRemotePath));
			}
		}

		internal int ConsolidateOldestBatch()
		{
			DebugLog("ConsolidateOldestBatch starting");

			try
			{
				// Find the two oldest batches.
				// If there is only one batch, nothing to do.
				var batchNumbers = new List<int>(_cacheStorage.EnumerateBatches());

				DebugLog("=> found {0} batch numbers", batchNumbers.Count);

				if (batchNumbers.Count < 2)
				{
					DebugLog("=> returning");
					return -1;
				}

				batchNumbers.Sort();

				var oldestBatchNumber = batchNumbers[0];
				var mergeIntoBatchNumber = batchNumbers[1];

				DebugLog("oldest batch number: {0}", oldestBatchNumber);
				DebugLog("merge into batch number: {0}", mergeIntoBatchNumber);

				// Load in the merge-into batch. If it contains any deletions of file states from the previous batch, we
				// need to make sure we don't merge those deleted entries in. But, we don't need to keep them because
				// after the consolidation, there won't be any older state that needs to be deleted in the first place.
				var mergeIntoBatch = new Dictionary<string, FileState>();

				var deletedPaths = new HashSet<string>();

				DebugLog("reading merge into batch number");

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

				DebugLog("underlaying oldest batch number");

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

				DebugLog("writing out merged batch");

				// Write out the merged batch. It is written to a ".new" file first, so that the update to the actual
				// batch file path is atomic and cannot possibly contain an incomplete file in the event of an error
				// or power loss or whatnot.
				using (var writer = _cacheStorage.OpenNewBatchFileWriter(mergeIntoBatchNumber))
					foreach (var fileState in mergeIntoBatch.Values)
						writer.WriteLine(fileState);

				DebugLog("switching to consolidated file");

				_cacheStorage.SwitchToConsolidatedFile(
					oldestBatchNumber,
					mergeIntoBatchNumber);

				DebugLog("queuing deletion of old version of {0}", mergeIntoBatchNumber);

				QueueAction(CacheAction.DeleteFile("/state/" + mergeIntoBatchNumber));

				DebugLog("calling UploadBatch on {0}", mergeIntoBatchNumber);

				UploadBatch(mergeIntoBatchNumber);

				// Return the batch number that no longer exists so that the caller can remove it from remote storage.
				return oldestBatchNumber;
			}
			catch (Exception exception)
			{
				_errorLogger.LogError("An error occurred while consolidating Remote File State Cache batches.", exception);
				throw;
			}
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
						DebugLog("[AT] action thread waiting");
						Monitor.Wait(_actionThreadSync);
						continue;
					}

					action = _actionQueue.Dequeue();

					DebugLog("[AT] checking if this action is redundant");

					bool isRedundant = false;

					if (action.CacheActionType == CacheActionType.UploadFile)
					{
						if (_actionQueue.Any(futureAction => (futureAction.Path == action.Path) && (futureAction.CacheActionType == CacheActionType.DeleteFile)))
						{
							DebugLog("[AT] => future action will delete this path anyway, no point in uploading");
							isRedundant = true;
						}
					}

					if (isRedundant)
					{
						if (File.Exists(action.SourcePath))
						{
							DebugLog("[AT] deleting source file: {0}", action.SourcePath);
							File.Delete(action.SourcePath);
						}
						else
							DebugLog("[AT] source file has gone missing anyway: {0}", action.SourcePath);
					}
					else
					{
						DebugLog("[AT] processing action");
						while (!action.IsComplete)
							ProcessCacheAction(action);
					}

					try
					{
						DebugLog("[AT] deleting action file");
						if (action.ActionFileName != null)
						{
							File.Delete(action.ActionFileName);
							action.ActionFileName = null;
						}
					}
					catch (Exception exception)
					{
						_errorLogger.LogError("An error occurred deleting an action file: " + action.ActionFileName, exception);
						DebugLog("[AT] => error deleting action file (logged)");
					}

					DebugLog("[AT] notifying anybody waiting that we achieved something");

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
						DebugLog("[PCA] performing upload of {0} to {1}", action.SourcePath, action.Path);

						for (int retry = 0; retry < 5; retry++)
						{
							if (!File.Exists(action.SourcePath))
							{
								_errorLogger.LogError(
									"The source file for a queued Upload File action in the Remote File State Cache has gone missing.\n" +
									"\n" +
									"Was expecting to upload: " + action.SourcePath + "\n" +
									"\n" +
									"Will upload a 0-byte dummy file instead. This will unblock the queue but means that the data server-side is incomplete. " +
									"A future batch upload should resolve this.");

								DebugLog("[PCA] ERROR (logged): source file has gone away! {0}", action.SourcePath);
								DebugLog("[PCA] uploading dummy file");

								action.SourcePath = Path.GetTempFileName();
							}

							try
							{
								using (var stream = File.OpenRead(action.SourcePath!))
									_remoteStorage.UploadFileDirect(action.Path!, stream, CancellationToken.None);
								break;
							}
							catch (Exception e) when (retry < 4)
							{
								DebugLog("[PCA] => upload failed: {0}: {1}", e.GetType().Name, e.Message);
								Thread.Sleep(TimeSpan.FromSeconds(0.5));
							}
						}

						DebugLog("[PCA] upload succeeded, deleting source file: {0}", action.SourcePath);
						File.Delete(action.SourcePath!);

						break;
					case CacheActionType.DeleteFile:
						DebugLog("[PCA] performing deletion of {0}", action.Path);

						for (int retry = 0; retry < 5; retry++)
						{
							try
							{
								_remoteStorage.DeleteFileDirect(action.Path!, CancellationToken.None);
							}
							catch (Exception e) when (retry < 4)
							{
								DebugLog("[PCA] => deletion failed: {0}: {1}", e.GetType().Name, e.Message);

								if (e is FileNotFoundException)
								{
									DebugLog("[PCA] => because the file already doesn't exist server-side");
									break;
								}
							}
						}

						break;
				}

				DebugLog("[PCA] complete");

				action.IsComplete = true;

				_cacheActionLog.ReleaseAction(action);
			}
			catch (Exception e)
			{
				DebugLog("[PCA] failed");
				DebugLog(e);

				Thread.Sleep(TimeSpan.FromSeconds(5));
			}
		}
	}
}

