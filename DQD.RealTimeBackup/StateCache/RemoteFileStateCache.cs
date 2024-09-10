using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.Utility;

using ITimer = DQD.RealTimeBackup.Utility.ITimer;

namespace DQD.RealTimeBackup.StateCache
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
		CancellationTokenSource _cancellationTokenSource;

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
							ErrorLogger.Summary.ImportantBackupError,
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
			WakeActionThread(hardCancel: true);
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

			// This only needs to be set when we start the action thread.
			_cancellationTokenSource = default!;
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
			=> EnumerateFileStates(null);

		public IEnumerable<FileState> EnumerateFileStates(Action<double>? progressCallback = null)
		{
			lock (_sync)
			{
				EnsureCacheIsLoaded(progressCallback);

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

		void EnsureCacheIsLoaded(Action<double>? progressCallback = null)
		{
			if (!_cacheLoaded)
				LoadCache(progressCallback);
			else
				progressCallback?.Invoke(1.0);
		}

		public void LoadCache()
			=> LoadCache(null);

		public void LoadCache(Action<double>? progressCallback = null)
		{
			DebugLog("loading cache");

			// Enumerate the batch numbers in storage. Process them in order.
			var batchFiles = new List<BatchFileInfo>(_cacheStorage.EnumerateBatches());

			batchFiles.Sort();

			DebugLog("found {0} batches", batchFiles.Count);

			// Inflate the size of each batch by a fixed amount, to account for the overhead
			// of switching from one file to the next.
			const int BatchFileExtraSize = 500_000;

			long[]? batchFileProgressContribution = null;
			Action<double>[]? batchFileProgressCallback = null;

			if (progressCallback != null)
			{
				batchFileProgressContribution = batchFiles
					.Select(batch => batch.FileSize + BatchFileExtraSize)
					.ToArray();

				batchFileProgressCallback = new Action<double>[batchFileProgressContribution.Length];

				long batchOverallOffset = 0;
				double allBatchesTotalSize = batchFileProgressContribution.Sum();

				for (int i=0; i < batchFileProgressContribution.Length; i++)
				{
					int thisBatchIndex = i;
					long thisBatchOffset = batchOverallOffset;

					batchFileProgressCallback[i] =
						(progress) =>
						{
							double thisFileCompletion = progress * batchFileProgressContribution[thisBatchIndex];

							double overallCompletion = thisBatchOffset + thisFileCompletion;

							double overallProgress = overallCompletion / allBatchesTotalSize;

							if (double.IsFinite(overallProgress))
								progressCallback(overallProgress);
						};

					batchOverallOffset += batchFileProgressContribution[i];
				}
			}

			// Load in all saved FileStates. If we encounter one that we already have in the cache,
			// it is a newer state that supersedes the previously-loaded one.
			for (int i=0; i < batchFiles.Count; i++)
			{
				var batchFile = batchFiles[i];
				Action<double>? thisBatchFileProgressCallback = (progressCallback != null)
					? batchFileProgressCallback![i]
					: default;

				int batchNumber = batchFile.BatchNumber;

				DebugLog("applying batch {0}", batchNumber);

				using (var stream = _cacheStorage.OpenBatchFileStream(batchNumber))
				using (var reader = new StreamReader(new ReadProgressStream(stream, batchFile.FileSize, thisBatchFileProgressCallback)))
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

			if (batchFiles.Any())
				_currentBatchNumber = batchFiles.Max()!.BatchNumber + 1;
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
				QueueUploadBatch(batchNumberToUpload);

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
				DebugLog("checking if consolidation is already happening");

				if (!Monitor.TryEnter(_consolidationSync))
					DebugLog("=> consolidation mutex is already held");
				else
				{
					try
					{
						DebugLog("considering consolidation");
						ConsolidateBatches();
					}
					finally
					{
						Monitor.Exit(_consolidationSync);
					}
				}
			}

			WakeActionThread(hardCancel: false);
		}

		internal void QueueUploadBatch(int batchNumberToUpload)
		{
			DebugLog("beginning UploadBatch of {0}", batchNumberToUpload);

			string batchRemotePath = "/state/" + batchNumberToUpload;

			var temporaryCopyPath = _cacheActionLog.CreateTemporaryCacheActionDataFile();

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

		internal static int ConsolidationBatchCountMinimum = 5;
		internal static int ConsolidationBatchBytesPerAdditionalBatch = 1048576;

		internal void ConsolidateBatches()
		{
			DebugLog("ConsolidateOldestBatch starting");

			try
			{
				// Find the oldest batches. There need to be enough batches that we won't
				// be touching the current batch that's in progress.
				var batchNumbers = new List<int>(_cacheStorage.EnumerateBatches()
					.Select(batchFile => batchFile.BatchNumber));

				DebugLog("=> found {0} batch numbers", batchNumbers.Count);

				int requiredConsolidationBatchCount = ConsolidationBatchCountMinimum;
				int consolidationBatchCount = 0;
				int totalBatchSizes = 0;

				foreach (int batchNumber in batchNumbers)
				{
					int batchSize = _cacheStorage.GetBatchFileSize(batchNumber);

					totalBatchSizes += batchSize;

					requiredConsolidationBatchCount = ConsolidationBatchCountMinimum + (totalBatchSizes + ConsolidationBatchBytesPerAdditionalBatch / 2) / ConsolidationBatchBytesPerAdditionalBatch;

					if (consolidationBatchCount + 1 >= batchNumbers.Count)
						break;

					consolidationBatchCount++;

					if (consolidationBatchCount >= requiredConsolidationBatchCount)
						break;
				}

				if (consolidationBatchCount < requiredConsolidationBatchCount)
				{
					DebugLog("=> not enough batches, returning");
					return;
				}

				batchNumbers.Sort();

				if (batchNumbers.Count > consolidationBatchCount)
					batchNumbers.RemoveRange(consolidationBatchCount, batchNumbers.Count - consolidationBatchCount);

				for (int i=0; i < batchNumbers.Count; i++)
				{
					if (i + 1 < batchNumbers.Count)
						DebugLog("will merge batch number: {0}", batchNumbers[i]);
					else
						DebugLog(".. into batch number: {0}", batchNumbers[i]);
				}

				var batchDeletionActions = new List<Action>();

				// Apply the batches in order.
				var mergedBatch = new Dictionary<string, FileState>();

				foreach (var mergeFromBatchNumber in batchNumbers)
				{
					DebugLog("merging batch number {0}", mergeFromBatchNumber);

					using (var reader = _cacheStorage.OpenBatchFileReader(mergeFromBatchNumber))
					{
						while (true)
						{
							string? line = reader.ReadLine();

							if (line == null)
								break;

							var fileState = FileState.Parse(line);

							if (fileState.FileSize == DeletedFileSize)
								mergedBatch.Remove(fileState.Path);
							else
								mergedBatch[fileState.Path] = fileState;
						}
					}
				}

				DebugLog("writing out merged batch");

				var mergeIntoBatchNumber = batchNumbers.Last();

				// Write out the merged batch. It is written to a ".new" file first, so that the update to the actual
				// batch file path is atomic and cannot possibly contain an incomplete file in the event of an error
				// or power loss or whatnot.
				using (var writer = _cacheStorage.OpenNewBatchFileWriter(mergeIntoBatchNumber))
					foreach (var fileState in mergedBatch.Values)
						writer.WriteLine(fileState);

				DebugLog("switching to consolidated file");

				_cacheStorage.SwitchToConsolidatedFile(
					batchNumbers.Where(batchNumber => batchNumber != mergeIntoBatchNumber),
					mergeIntoBatchNumber);

				DebugLog("queuidng upload of batch {0} to remote storage", mergeIntoBatchNumber);

				QueueUploadBatch(mergeIntoBatchNumber);

				for (int i = 0; i < batchNumbers.Count; i++)
				{
					int mergeFromBatchNumber = batchNumbers[i];

					if (mergeFromBatchNumber != mergeIntoBatchNumber)
					{
						DebugLog("queuing deletion of batch {0} from remote storage", mergeFromBatchNumber);

						QueueAction(CacheAction.DeleteFile("/state/" + mergeFromBatchNumber));
					}
				}

				WakeActionThread(hardCancel: false);
			}
			catch (Exception exception)
			{
				_errorLogger.LogError("An error occurred while consolidating Remote File State Cache batches.", ErrorLogger.Summary.InternalError, exception);
				throw;
			}
		}

		void StartActionThread()
		{
			_cancellationTokenSource = new CancellationTokenSource();

			Thread thread = new Thread(ActionThreadProc);

			thread.Name = "Remote File State Cache Action Thread";
			thread.Start();
		}

		void WakeActionThread(bool hardCancel)
		{
			if (hardCancel)
				_cancellationTokenSource.Cancel();

			lock (_actionThreadSync)
				Monitor.PulseAll(_actionThreadSync);
		}

		void QueueAction(CacheAction action)
		{
			_cacheActionLog.LogAction(action);

			lock (_actionThreadSync)
				_actionQueue.Enqueue(action);
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
			try
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

							Monitor.Exit(_actionThreadSync);

							try
							{
								while (!action.IsComplete && !_stopping)
									ProcessCacheAction(action);
							}
							finally
							{
								Monitor.Enter(_actionThreadSync);
							}

							if (_stopping && !action.IsComplete)
							{
								DebugLog("[AT] stopping, leaving incomplete action's file");
								break;
							}
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
							_errorLogger.LogError("An error occurred deleting an action file: " + action.ActionFileName, ErrorLogger.Summary.SystemError, exception);
							DebugLog("[AT] => error deleting action file (logged)");
						}

						DebugLog("[AT] notifying anybody waiting that we achieved something");

						Monitor.PulseAll(_actionThreadSync);
					}
				}
			}
			catch (Exception e)
			{
				_errorLogger.LogError(
					"The Remote File State Cache Action Queue has crashed",
					"The thread in charge of pushing updates to the Remote File State Cache to remote storage has crashed. The thread will " +
					"be restarted in 30 seconds.",
					e);

				Thread.Sleep(TimeSpan.FromSeconds(30));

				StartActionThread();
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

						try
						{
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
										"A future batch upload should resolve this.",
										ErrorLogger.Summary.ImportantBackupError);

									DebugLog("[PCA] ERROR (logged): source file has gone away! {0}", action.SourcePath);
									DebugLog("[PCA] uploading dummy file");

									action.SourcePath = Path.GetTempFileName();
								}

								try
								{
									using (var stream = File.OpenRead(action.SourcePath!))
										_remoteStorage.UploadFileDirect(action.Path!, stream, _cancellationTokenSource.Token);
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
						}
						catch (TaskCanceledException)
						{
							DebugLog("[PCA] => upload cancelled, leaving file for next run");
							return;
						}

						break;
					case CacheActionType.DeleteFile:
						DebugLog("[PCA] performing deletion of {0}", action.Path);

						for (int retry = 0; retry < 5; retry++)
						{
							try
							{
								_remoteStorage.DeleteFileDirect(action.Path!, CancellationToken.None);
								break;
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

				_errorLogger.LogError(
					"Remote File State Cache action failed",
					"An action related to synchronizing the Remote File State Cache with remote storage has failed. The action will be retried.",
					e);

				if (!_stopping)
					Thread.Sleep(TimeSpan.FromSeconds(5));
			}
		}
	}
}

