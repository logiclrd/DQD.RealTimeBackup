using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using DeltaQ.RTB.ActivityMonitor;
using DeltaQ.RTB.FileSystem;
using DeltaQ.RTB.Interop;
using DeltaQ.RTB.StateCache;
using DeltaQ.RTB.Storage;
using DeltaQ.RTB.Utility;

using ITimer = DeltaQ.RTB.Utility.ITimer;

namespace DeltaQ.RTB.Agent
{
	public class BackupAgent : IBackupAgent
	{
		// - get events from FileSystemMonitor
		// - check for open file handles with OpenFileHandles
		// - find or create a ZFS snapshot
		// - copy file to staging area (if not too large) & enqueue
		// - release ZFS snapshot

		OperatingParameters _parameters;

		bool _stopping = false;

		ITimer _timer;
		IChecksum _checksum;
		IFileSystemMonitor _monitor;
		IOpenFileHandles _openFileHandles;
		IZFS _zfs;
		IStaging _staging;
		IRemoteFileStateCache _remoteFileStateCache;
		IRemoteStorage _storage;

		public BackupAgent(OperatingParameters parameters, ITimer timer, IChecksum checksum, IFileSystemMonitor monitor, IOpenFileHandles openFileHandles, IZFS zfs, IStaging staging, IRemoteFileStateCache remoteFileStateCache, IRemoteStorage storage)
		{
			_parameters = parameters;

			_timer = timer;
			_checksum = checksum;
			_monitor = monitor;
			_openFileHandles = openFileHandles;
			_zfs = zfs;
			_staging = staging;
			_remoteFileStateCache = remoteFileStateCache;
			_storage = storage;

			_monitor.PathUpdate += monitor_PathUpdate;
			_monitor.PathMove += monitor_PathMove;
			_monitor.PathDelete += monitor_PathDelete;
		}

		void monitor_PathUpdate(object? sender, PathUpdate update)
		{
			BeginQueuePathForOpenFilesCheck(update.Path);
		}

		void monitor_PathMove(object? sender, PathMove move)
		{
			AddActionToBackupQueue(new MoveAction(move.PathFrom, move.PathTo));
		}

		void monitor_PathDelete(object? sender, PathDelete delete)
		{
			AddActionToBackupQueue(new DeleteAction(delete.Path));
		}

		object _snapshotSharingDelaySync = new object();
		ITimerInstance? _snapshotSharingDelay;
		List<string> _snapshotSharingBatch = new List<string>();

		void BeginQueuePathForOpenFilesCheck(string path)
		{
			lock (_snapshotSharingDelaySync)
			{
				if (_snapshotSharingDelay == null)
					_snapshotSharingDelay = _timer.ScheduleAction(_parameters.SnapshotSharingWindow, EndQueuePathForOpenFilesCheck);

				_snapshotSharingBatch.Add(path);
			}
		}

		void EndQueuePathForOpenFilesCheck()
		{
			lock (_snapshotSharingDelaySync)
			{
				_snapshotSharingDelay?.Dispose();
				_snapshotSharingDelay = null;

				var snapshot = _zfs.CreateSnapshot("RTB-" + DateTime.UtcNow.Ticks);

				var snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot);

				foreach (string path in _snapshotSharingBatch)
					QueuePathForOpenFilesCheck(snapshotReferenceTracker.AddReference(path));

				_snapshotSharingBatch.Clear();
			}
		}

		class SnapshotReferenceWithTimeout
		{
			public SnapshotReference SnapshotReference;
			public DateTime TimeoutUTC;

			public SnapshotReferenceWithTimeout(SnapshotReference snapshotReference)
			{
				this.SnapshotReference = snapshotReference;
			}
		}

		object _openFilesSync = new object();
		List<SnapshotReferenceWithTimeout> _openFiles = new List<SnapshotReferenceWithTimeout>();

		internal void QueuePathForOpenFilesCheck(SnapshotReference reference)
		{
			lock (_openFilesSync)
			{
				var withTimeout = new SnapshotReferenceWithTimeout(reference);

				withTimeout.TimeoutUTC = DateTime.UtcNow + _parameters.MaximumTimeToWaitForNoOpenFileHandles;

				_openFiles.Add(withTimeout);
				Monitor.PulseAll(_openFilesSync);
			}
		}

		object _openFileHandlePollingSync = new object();

		internal void StartPollOpenFilesThread()
		{
			new Thread(PollOpenFilesThreadProc).Start();
		}

		void WakePollOpenFilesThread()
		{
			// The open file handles polling thread waits on _openFilesSync when its queue is empty, and
			// on _openFileHandlePollingSync when its queue is not empty.
			lock (_openFilesSync)
				Monitor.PulseAll(_openFilesSync);

			lock (_openFileHandlePollingSync)
				Monitor.PulseAll(_openFileHandlePollingSync);
		}

		void PollOpenFilesThreadProc()
		{
			var openFilesCached = new List<SnapshotReferenceWithTimeout>();
			var filesToPromote = new List<SnapshotReferenceWithTimeout>();

			while (!_stopping)
			{
				bool haveOpenFiles = false;

				lock (_openFilesSync)
				{
					haveOpenFiles = (_openFiles.Count != 0);

					// If we don't have any open files, wait indefinitely but break as soon as open files become available.
					if (!haveOpenFiles)
					{
						Monitor.Wait(_openFilesSync);

						if (_stopping)
							return;

						openFilesCached.Clear();
						openFilesCached.AddRange(_openFiles);
					}
				}

				// As long as we have open files, always wait for the interval between checks.
				if (haveOpenFiles)
				{
					lock (_openFileHandlePollingSync)
						Monitor.Wait(_openFileHandlePollingSync, _parameters.OpenFileHandlePollingInterval);

					if (_stopping)
						return;

					openFilesCached.Clear();

					lock (_openFilesSync)
						openFilesCached.AddRange(_openFiles);
				}

				for (int i = openFilesCached.Count - 1; i >= 0; i--)
				{
					var fileReference = openFilesCached[i];

					if (!_openFileHandles.Enumerate(fileReference.SnapshotReference.Path).Any(handle => handle.FileAccess.HasFlag(FileAccess.Write)))
					{
						filesToPromote.Add(fileReference);
						openFilesCached.RemoveAt(i);
					}
				}

				if (filesToPromote.Any())
				{
					lock (_openFilesSync)
						foreach (var reference in filesToPromote)
							_openFiles.Remove(reference);

					AddActionsToBackupQueue(filesToPromote
						.Select(reference => reference.SnapshotReference)
						.Select(reference => new UploadAction(reference, reference.Path)));

					filesToPromote.Clear();
				}

				bool haveTimedOutFiles = false;

				var now = DateTime.UtcNow;

				foreach (var file in openFilesCached)
					if (file.TimeoutUTC < now)
					{
						haveTimedOutFiles = true;
						break;
					}

				if (haveTimedOutFiles)
				{
					lock (_openFilesSync)
					{
						for (int i = _openFiles.Count - 1; i >= 0; i--)
						{
							if (_openFiles[i].TimeoutUTC < now)
							{
								AddLongPollingItem(_openFiles[i].SnapshotReference);
								_openFiles.RemoveAt(i);
							}
						}
					}
				}
			}
		}

		class LongPollingItem
		{
			public SnapshotReference CurrentSnapshotReference;
			public DateTime DeadlineUTC; // Upload anyway after this has elapsed.

			public LongPollingItem(SnapshotReference snapshotReference, TimeSpan timeout)
			{
				this.CurrentSnapshotReference = snapshotReference;
				this.DeadlineUTC = DateTime.UtcNow + timeout;
			}
		}

		object _longPollingSync = new object();
		List<LongPollingItem> _longPollingQueue = new List<LongPollingItem>();
		CancellationTokenSource? _longPollingCancellationTokenSource;

		void StartLongPollingThread()
		{
			_longPollingCancellationTokenSource = new CancellationTokenSource();

			new Thread(LongPollingThreadProc).Start();
		}

		void WakeLongPollingThread()
		{
			lock (_longPollingSync)
				Monitor.PulseAll(_longPollingSync);
		}

		void CleanUpLongPollingThread()
		{
			_longPollingCancellationTokenSource?.Dispose();
			_longPollingCancellationTokenSource = null;
		}

		void AddLongPollingItem(SnapshotReference snapshotReference)
		{
			lock (_longPollingSync)
			{
				_longPollingQueue.Add(new LongPollingItem(snapshotReference, _parameters.MaximumLongPollingTime));
				Monitor.PulseAll(_longPollingSync);
			}
		}

		void LongPollingThreadProc()
		{
			DateTime intervalEndUTC = DateTime.UtcNow;

			while (!_stopping)
			{
				List<BackupAction>? actions = null;

				lock (_longPollingSync)
				{
					if (_longPollingQueue.Count == 0)
					{
						Monitor.Wait(_longPollingSync);
						continue;
					}

					var maximumDeadLineUTC = DateTime.UtcNow + _parameters.LongPollingInterval;

					if (_longPollingQueue.Count == 0)
						intervalEndUTC = maximumDeadLineUTC;
					else
					{
						intervalEndUTC = _longPollingQueue.Select(entry => entry.DeadlineUTC).Min();

						if (intervalEndUTC > maximumDeadLineUTC)
							intervalEndUTC = maximumDeadLineUTC;
					}

					var waitDuration = intervalEndUTC - DateTime.UtcNow;

					while ((waitDuration > TimeSpan.Zero) && !_stopping)
					{
						Monitor.Wait(_longPollingSync, waitDuration);
						waitDuration = intervalEndUTC - DateTime.UtcNow;
					}

					if (_stopping)
						break;

					var now = DateTime.UtcNow;

					var snapshot = _zfs.CreateSnapshot("RTB-" + now.Ticks);

					var snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot);

					for (int i = _longPollingQueue.Count - 1; i >= 0; i--)
					{
						var item = _longPollingQueue[i];

						var newSnapshotReference = new SnapshotReference(
							snapshotReferenceTracker,
							item.CurrentSnapshotReference.Path);

						bool promoteFile = false;

						if ((item.DeadlineUTC < now)
						 || !_openFileHandles.Enumerate(newSnapshotReference.Path).Any(handle => handle.FileAccess.HasFlag(FileAccess.Write)))
						{
							item.CurrentSnapshotReference.Dispose();
							promoteFile = true;
						}
						else
						{
							bool fileLooksStable = FileUtility.FilesAreEqual(
								item.CurrentSnapshotReference.SnapshottedPath,
								newSnapshotReference.SnapshottedPath,
								_longPollingCancellationTokenSource?.Token ?? CancellationToken.None);

							item.CurrentSnapshotReference.Dispose();
							item.CurrentSnapshotReference = newSnapshotReference;

							if (fileLooksStable)
								promoteFile = true;
						}

						if (promoteFile)
						{
							_longPollingQueue.RemoveAt(i);

							if (actions == null)
								actions = new List<BackupAction>();

							actions.Add(new UploadAction(newSnapshotReference, newSnapshotReference.Path));
						}
					}
				}

				if ((actions != null) && !_stopping)
					AddActionsToBackupQueue(actions);
			}
		}

		object _backupQueueSync = new object();
		List<BackupAction> _backupQueue = new List<BackupAction>();
		CancellationTokenSource? _backupQueueCancellationTokenSource;
		ManualResetEvent? _backupQueueExited;

		void AddActionToBackupQueue(BackupAction action)
		{
			lock (_backupQueueSync)
			{
				_backupQueue.Add(action);
				Monitor.PulseAll(_backupQueueSync);
			}
		}

		void AddActionsToBackupQueue(IEnumerable<BackupAction> actions)
		{
			lock (_backupQueueSync)
			{
				_backupQueue.AddRange(actions);
				Monitor.PulseAll(_backupQueueSync);
			}
		}

		internal IEnumerable<BackupAction> BackupQueue => _backupQueue.Select(x => x);

		void StartProcessBackupQueueThread()
		{
			_backupQueueCancellationTokenSource = new CancellationTokenSource();
			_backupQueueExited = new ManualResetEvent(initialState: false);

			new Thread(ProcessBackupQueueThreadProc).Start();
		}

		void WakeProcessBackupQueueThread()
		{
			lock (_backupQueueSync)
				Monitor.PulseAll(_backupQueueSync);
		}

		void InterruptProcessBackupQueueThread()
		{
			_backupQueueCancellationTokenSource?.Cancel();

			WakeProcessBackupQueueThread();
		}

		bool WaitForProcessBackupQueueThreadToExit(TimeSpan timeout)
		{
			return _backupQueueExited?.WaitOne(timeout) ?? true;
		}

		void CleanUpProcessBackupQueueThread()
		{
			_backupQueueCancellationTokenSource?.Dispose();
			_backupQueueCancellationTokenSource = null;

			_backupQueueExited?.Dispose();
			_backupQueueExited = null;
		}

		void ProcessBackupQueueThreadProc()
		{
			try
			{
				while (!_stopping)
				{
					BackupAction backupAction;

					lock (_backupQueueSync)
					{
						while (_backupQueue.Count == 0)
						{
							if (_stopping)
								return;

							Monitor.Wait(_backupQueueSync);
						}

						if (_stopping)
							return;

						backupAction = _backupQueue[_backupQueue.Count - 1];
						_backupQueue.RemoveAt(_backupQueue.Count - 1);
					}

					ProcessBackupQueueAction(backupAction);
				}
			}
			finally
			{
				_backupQueueExited?.Set();
			}
		}

		internal void ProcessBackupQueueAction(BackupAction action)
		{
			switch (action)
			{
				case UploadAction uploadAction:
					FileReference fileReference;

					var stream = File.OpenRead(uploadAction.Source.SnapshottedPath);

					var backedUpFileState = _remoteFileStateCache.GetFileState(uploadAction.Source.Path);
					var currentLocalFileChecksum = _checksum.ComputeChecksum(stream);

					if ((backedUpFileState == null) || (currentLocalFileChecksum != backedUpFileState.Checksum))
					{
						if (stream.Length > _parameters.MaximumFileSizeForStagingCopy)
							fileReference = new FileReference(uploadAction.Source, stream);
						else
						{
							var stagedCopy = _staging.StageFile(stream);

							stream.Dispose();

							fileReference = new FileReference(uploadAction.Source.Path, stagedCopy);

							uploadAction.Source.Dispose();
						}

						AddFileReferenceToUploadQueue(fileReference);
					}

					break;
				case MoveAction moveAction:
					_storage.MoveFile(
						moveAction.FromPath,
						moveAction.ToPath,
						_backupQueueCancellationTokenSource?.Token ?? CancellationToken.None);

					break;
				case DeleteAction deleteAction:
					_storage.DeleteFile(
						deleteAction.Path,
						_backupQueueCancellationTokenSource?.Token ?? CancellationToken.None);

					break;
			}
			/*
       * Restrict number of concurrent tasks (separately for small vs. large files)
       * */
		}

		object _uploadQueueSync = new object();
		CancellationTokenSource? _cancelUploadsCancellationTokenSource;
		List<FileReference> _uploadQueue = new List<FileReference>();
		int _uploadThreadCount;
		Semaphore? _uploadThreadsExited;

		void StartUploadThreads()
		{
			_uploadThreadCount = _parameters.UploadThreadCount;
			_uploadThreadsExited = new Semaphore(initialCount: 0, maximumCount: _uploadThreadCount);

			_cancelUploadsCancellationTokenSource = new CancellationTokenSource();

			for (int i = 0; i < _parameters.UploadThreadCount; i++)
				new Thread(() => UploadThreadProc(_cancelUploadsCancellationTokenSource.Token)).Start();
		}

		void InterruptUploadThreads()
		{
			// Idle Upload threads wait on _uploadQueueSync.
			lock (_uploadQueueSync)
				Monitor.PulseAll(_uploadQueueSync);

			// Busy upload threads can be interrupted with the cancellation token.
			_cancelUploadsCancellationTokenSource?.Cancel();
		}

		void WaitForUploadThreadsToExit()
		{
			var exited = _uploadThreadsExited;

			if (exited != null)
				for (int i = 0, l = _uploadThreadCount; i < l; i++)
					exited.WaitOne();
		}

		void CleanUpUploadThreads()
		{
			_cancelUploadsCancellationTokenSource?.Dispose();
			_cancelUploadsCancellationTokenSource = null;

			_uploadThreadsExited?.Dispose();
			_uploadThreadsExited = null;
		}


		public IEnumerable<FileReference> PeekUploadQueue()
		{
			lock (_uploadQueueSync)
				return _uploadQueue.ToList();
		}

		internal void AddFileReferenceToUploadQueue(FileReference fileReference)
		{
			lock (_uploadQueueSync)
			{
				_uploadQueue.Add(fileReference);
				Monitor.PulseAll(_uploadQueueSync);
			}
		}

		void UploadThreadProc(CancellationToken cancellationToken)
		{
			var exited = _uploadThreadsExited;

			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					FileReference fileToUpload;

					lock (_uploadQueueSync)
					{
						while (_uploadQueue.Count == 0)
						{
							Monitor.Wait(_uploadQueueSync);

							if (_stopping)
								return;
						}

						fileToUpload = _uploadQueue[_uploadQueue.Count - 1];
						_uploadQueue.RemoveAt(_uploadQueue.Count - 1);
					}

					using (fileToUpload)
					{
						// TODO: write down that we're now uploading this file so that if we get interrupted, we can pick up where we left off
						//
						_storage.UploadFile(Path.Combine("/content", fileToUpload.Path), fileToUpload.Stream, cancellationToken);
					}
				}
			}
			finally
			{
				exited?.Release();
			}
		}

		public void Start()
		{
			_stopping = false;
			_monitor.Start();

			if (_parameters.EnableFileAccessNotify)
			{
				StartPollOpenFilesThread();
				StartLongPollingThread();
			}

			StartProcessBackupQueueThread();
			StartUploadThreads();
		}

		public void Stop()
		{
			_stopping = true;

			_monitor.Stop();
			_remoteFileStateCache.Stop();

			WakePollOpenFilesThread();
			WakeLongPollingThread();
			WakeProcessBackupQueueThread();

			_remoteFileStateCache.WaitWhileBusy();

			if (!WaitForProcessBackupQueueThreadToExit(TimeSpan.FromSeconds(3)))
			{
				InterruptProcessBackupQueueThread();
				WaitForProcessBackupQueueThreadToExit(TimeSpan.FromSeconds(1));
			}

			// We don't need to sync to the open file handles polling thread exiting because it doesn't take actions.

			InterruptUploadThreads();

			WaitForUploadThreadsToExit();

			CleanUpLongPollingThread();
			CleanUpProcessBackupQueueThread();
			CleanUpUploadThreads();
		}
	}
}

