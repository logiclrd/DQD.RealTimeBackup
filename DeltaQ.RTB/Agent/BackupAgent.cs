using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using DeltaQ.RTB.ActivityMonitor;
using DeltaQ.RTB.FileSystem;
using DeltaQ.RTB.Interop;
using DeltaQ.RTB.StateCache;
using DeltaQ.RTB.Storage;
using DeltaQ.RTB.SurfaceArea;
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
		ISurfaceArea _surfaceArea;
		IFileSystemMonitor _monitor;
		IOpenFileHandles _openFileHandles;
		IZFS _zfs;
		IStaging _staging;
		IRemoteFileStateCache _remoteFileStateCache;
		IRemoteStorage _storage;

		List<(string MountPoint, IZFS ZFS)> _zfsInstanceByMountPoint; // Sorted by decreasing length of MountPoint

		public BackupAgent(OperatingParameters parameters, ITimer timer, IChecksum checksum, ISurfaceArea surfaceArea, IFileSystemMonitor monitor, IOpenFileHandles openFileHandles, IZFS zfs, IStaging staging, IRemoteFileStateCache remoteFileStateCache, IRemoteStorage storage)
		{
			_parameters = parameters;

			_timer = timer;
			_checksum = checksum;
			_surfaceArea = surfaceArea;
			_monitor = monitor;
			_openFileHandles = openFileHandles;
			_zfs = zfs;
			_staging = staging;
			_remoteFileStateCache = remoteFileStateCache;
			_storage = storage;

			_zfsInstanceByMountPoint = new List<(string MountPoint, IZFS ZFS)>();

			_monitor.PathUpdate += monitor_PathUpdate;
			_monitor.PathMove += monitor_PathMove;
			_monitor.PathDelete += monitor_PathDelete;
		}

		void VerboseWriteLine(object line)
		{
			if (_parameters.Verbose)
				Console.WriteLine(line);
		}

		void VerboseWriteLine(string format, params object?[] args)
		{
			if (_parameters.Verbose)
				Console.WriteLine(format, args);
		}

		void NonQuietWriteLine(object line)
		{
			if (!_parameters.Quiet)
				Console.WriteLine(line);
		}

		void NonQuietWriteLine(string format, params object?[] args)
		{
			if (!_parameters.Quiet)
				Console.WriteLine(format, args);
		}

		static string PlaceInContentPath(string path)
		{
			if (path.StartsWith("/"))
				return "/content" + path;
			else
				return "/content/" + path;
		}

		void monitor_PathUpdate(object? sender, PathUpdate update)
		{
			if (_paused)
			{
				lock (_pauseSync)
				{
					if (_paused)
					{
						_pathsWithActivityWhilePaused.Add(update.Path);
						return;
					}
				}
			}

			BeginQueuePathForOpenFilesCheck(update.Path);
		}

		void monitor_PathMove(object? sender, PathMove move)
		{
			if (_paused)
			{
				lock (_pauseSync)
				{
					if (_paused)
					{
						// This effectively collapses a move event into a delete and a recreate, but this only happens while paused (i.e., during initial backup).
						_pathsWithActivityWhilePaused.Add(move.PathFrom);
						_pathsWithActivityWhilePaused.Add(move.PathTo);
						return;
					}
				}
			}

			AddActionToBackupQueue(new MoveAction(move.PathFrom, move.PathTo));
		}

		void monitor_PathDelete(object? sender, PathDelete delete)
		{
			if (_paused)
			{
				lock (_pauseSync)
				{
					if (_paused)
					{
						_pathsWithActivityWhilePaused.Add(delete.Path);
						return;
					}
				}
			}

			AddActionToBackupQueue(new DeleteAction(delete.Path));
		}

		public void PauseMonitor()
		{
			lock (_pauseSync)
			{
				_paused = true;
				_pathsWithActivityWhilePaused.Clear();
			}
		}

		public void UnpauseMonitor()
		{
			lock (_pauseSync)
			{
				string[] capturedPaths = _pathsWithActivityWhilePaused.ToArray();

				_pathsWithActivityWhilePaused.Clear();

				_paused = false;

				foreach (string path in capturedPaths)
				{
					try
					{
						Monitor.Exit(_pauseSync);

						if (File.Exists(path))
							BeginQueuePathForOpenFilesCheck(path);
						else
							AddActionToBackupQueue(new DeleteAction(path));
					}
					finally
					{
						Monitor.Enter(_pauseSync);
					}
				}
			}
		}

		object _pauseSync = new object();
		bool _paused;
		HashSet<string> _pathsWithActivityWhilePaused = new HashSet<string>();

		public BackupAgentQueueSizes GetQueueSizes()
		{
			var queueSizes = new BackupAgentQueueSizes();

			lock (_snapshotSharingDelaySync)
				queueSizes.NumberOfFilesPendingIntake = _snapshotSharingBatch.Count;
			lock (_openFilesSync)
				queueSizes.NumberOfFilesPollingOpenHandles = _openFiles.Count;
			lock (_longPollingSync)
				queueSizes.NumberOfFilesPollingContentChanges = _longPollingQueue.Count;
			lock (_backupQueueSync)
				queueSizes.NumberOfBackupQueueActions = _backupQueue.Count;
			lock (_uploadQueueSync)
				queueSizes.NumberOfQueuedUploads = _uploadQueue.Count;

			return queueSizes;
		}

		public void CheckPath(string path)
		{
			if (File.Exists(path))
				BeginQueuePathForOpenFilesCheck(path);
			else if (_remoteFileStateCache.ContainsPath(path))
				AddActionToBackupQueue(new DeleteAction(path));
		}

		public void CheckPaths(IEnumerable<string> paths)
		{
			BeginQueuePathsForOpenFilesCheckAndCheckForDeletions(paths);
		}

		public void NotifyMove(string fromPath, string toPath)
		{
			AddActionToBackupQueue(new MoveAction(fromPath, toPath));
		}

		object _snapshotSharingDelaySync = new object();
		ITimerInstance? _snapshotSharingDelay;
		List<string> _snapshotSharingBatch = new List<string>();

		void BeginQueuePathForOpenFilesCheck(string path)
		{
			if (_parameters.IsExcludedPath(path))
				return;

			lock (_snapshotSharingDelaySync)
			{
				if (_snapshotSharingDelay == null)
				{
					VerboseWriteLine("[IN] Setting timer to end the queue path operation");
					_snapshotSharingDelay = _timer.ScheduleAction(_parameters.SnapshotSharingWindow, EndQueuePathForOpenFilesCheck);
				}

				_snapshotSharingBatch.Add(path);
			}
		}

		void BeginQueuePathsForOpenFilesCheckAndCheckForDeletions(IEnumerable<string> paths)
		{
			List<DeleteAction>? deleteActions = null;

			lock (_snapshotSharingDelaySync)
			{
				bool haveAdditions = false;

				foreach (string path in paths)
				{
					if (_parameters.IsExcludedPath(path))
						continue;

					if (File.Exists(path))
					{
						_snapshotSharingBatch.Add(path);
						haveAdditions = true;
					}
					else if (_remoteFileStateCache.ContainsPath(path))
					{
						deleteActions ??= new List<DeleteAction>();
						deleteActions.Add(new DeleteAction(path));
					}
				}

				if (haveAdditions && (_snapshotSharingDelay == null))
					_snapshotSharingDelay = _timer.ScheduleAction(_parameters.SnapshotSharingWindow, EndQueuePathForOpenFilesCheck);
			}

			if (deleteActions != null)
				AddActionsToBackupQueue(deleteActions);
		}

		IZFS? FindZFSVolumeForPath(string path)
		{
			foreach (var instance in _zfsInstanceByMountPoint)
			{
				if ((path.Length > instance.MountPoint.Length)
				 && ((instance.MountPoint == "/")
				  || ((path[instance.MountPoint.Length] == '/') && path.StartsWith(instance.MountPoint))))
					return instance.ZFS;
			}

			return null;
		}

		void EndQueuePathForOpenFilesCheck()
		{
			lock (_snapshotSharingDelaySync)
			{
				VerboseWriteLine("[IN] End queue path operation, collecting batch");

				_snapshotSharingDelay?.Dispose();
				_snapshotSharingDelay = null;

				var snapshots = new Dictionary<IZFS, SnapshotReferenceTracker>();

				foreach (string path in _snapshotSharingBatch)
				{
					VerboseWriteLine("[IN] => {0}", path);

					var zfs = FindZFSVolumeForPath(path);

					if (zfs == null)
					{
						Console.Error.WriteLine("[IN] Can't process file because it doesn't appear to be on a ZFS volume: {0}", path);
						continue;
					}

					if (!snapshots.TryGetValue(zfs, out var snapshotReferenceTracker))
					{
						VerboseWriteLine("[IN]   * ZFS snapshot needed on volume {0}", zfs.MountPoint);

						var snapshot = zfs.CreateSnapshot("RTB-" + DateTime.UtcNow.Ticks);

						snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot);

						snapshots[zfs] = snapshotReferenceTracker;
					}

					VerboseWriteLine("[IN]   * Queuing path for open files check");

					QueuePathForOpenFilesCheck(snapshotReferenceTracker.AddReference(path));
				}

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

				VerboseWriteLine("[->OF] Path queued with timeout {0}", withTimeout.TimeoutUTC);

				_openFiles.Add(withTimeout);
				Monitor.PulseAll(_openFilesSync);
			}
		}

		object _openFileHandlePollingSync = new object();

		internal void StartPollOpenFilesThread()
		{
			new Thread(PollOpenFilesThreadProc) { Name = "Poll Open Files Thread" }.Start();
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
			VerboseWriteLine("[OF] Thread started");

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
						VerboseWriteLine("[OF] Going to sleep");

						Monitor.Wait(_openFilesSync);

						VerboseWriteLine("[OF] Woken up");

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
					{
						VerboseWriteLine("[OF] Waiting until next check: {0}", _parameters.OpenFileHandlePollingInterval);
						Monitor.Wait(_openFileHandlePollingSync, _parameters.OpenFileHandlePollingInterval);
					}

					if (_stopping)
						return;

					openFilesCached.Clear();

					lock (_openFilesSync)
						openFilesCached.AddRange(_openFiles);
				}

				VerboseWriteLine("[OF] Inspecting {0} file(s)", openFilesCached.Count);

				for (int i = openFilesCached.Count - 1; i >= 0; i--)
				{
					var fileReference = openFilesCached[i];

					if (!_openFileHandles.Enumerate(fileReference.SnapshotReference.Path).Any(handle => handle.FileAccess.HasFlag(FileAccess.Write)))
					{
						VerboseWriteLine("[OF] => Ready: {0}", fileReference.SnapshotReference.Path);

						filesToPromote.Add(fileReference);
						openFilesCached.RemoveAt(i);
					}
				}

				if (filesToPromote.Any())
				{
					VerboseWriteLine("[OF] Promoting {0} file(s)", filesToPromote.Count);

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

			new Thread(LongPollingThreadProc) { Name = "Long Polling Thread" }.Start();
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
				VerboseWriteLine("[BQ] Queuing {0}", action.GetType().Name);

				_backupQueue.Add(action);
				Monitor.PulseAll(_backupQueueSync);
			}
		}

		void AddActionsToBackupQueue(IEnumerable<BackupAction> actions)
		{
			lock (_backupQueueSync)
			{
				VerboseWriteLine("[BQ] Queuing {0} actions", actions.Count());

				_backupQueue.AddRange(actions);
				Monitor.PulseAll(_backupQueueSync);
			}
		}

		internal IEnumerable<BackupAction> BackupQueue => _backupQueue.Select(x => x);

		void StartProcessBackupQueueThread()
		{
			_backupQueueCancellationTokenSource = new CancellationTokenSource();
			_backupQueueExited = new ManualResetEvent(initialState: false);

			new Thread(ProcessBackupQueueThreadProc) { Name = "Process Backup Queue Thread" }.Start();
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

							VerboseWriteLine("[BQ] Going to sleep");

							Monitor.Wait(_backupQueueSync);

							VerboseWriteLine("[BQ] Woken up");
						}

						if (_stopping)
							return;

						backupAction = _backupQueue[_backupQueue.Count - 1];
						_backupQueue.RemoveAt(_backupQueue.Count - 1);
					}

					VerboseWriteLine("[BQ] Dispatching: {0}", backupAction);

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
			VerboseWriteLine("[BQ] Beginning processing of backup action");

			switch (action)
			{
				case UploadAction uploadAction:
					VerboseWriteLine("[BQ] => UploadAction");
					VerboseWriteLine("[BQ]   * Path: {0}", uploadAction.ToPath);
					VerboseWriteLine("[BQ]   * Snapshotted Path: {0}", uploadAction.Source.SnapshottedPath);

					FileReference fileReference;

					var stream = File.OpenRead(uploadAction.Source.SnapshottedPath);

					var backedUpFileState = _remoteFileStateCache.GetFileState(uploadAction.Source.Path);
					var currentLocalFileChecksum = _checksum.ComputeChecksum(stream);

					if ((backedUpFileState != null) && (currentLocalFileChecksum == backedUpFileState.Checksum))
						VerboseWriteLine("[BQ] Remote File State Cache says this exact file is already uploaded, skipping");
					else
					{
						var currentLastModifiedUTC = File.GetLastWriteTimeUtc(uploadAction.Source.Path);

						if (stream.Length > _parameters.MaximumFileSizeForStagingCopy)
						{
							VerboseWriteLine("[BQ] Large file, uploading from snapshot");
							fileReference = new FileReference(uploadAction.Source, stream, currentLastModifiedUTC, currentLocalFileChecksum);
						}
						else
						{
							VerboseWriteLine("[BQ] Small file, staging file and releasing snapshot");

							var stagedCopy = _staging.StageFile(stream);

							stream.Dispose();

							fileReference = new FileReference(uploadAction.Source.Path, stagedCopy, currentLastModifiedUTC, currentLocalFileChecksum);

							uploadAction.Source.Dispose();
						}

						AddFileReferenceToUploadQueue(fileReference);
					}

					break;
				case MoveAction moveAction:
					VerboseWriteLine("[BQ] => MoveAction");
					VerboseWriteLine("[BQ]   * From Path: {0}", moveAction.FromPath);
					VerboseWriteLine("[BQ]   * To Path: {0}", moveAction.ToPath);

					var fileState = _remoteFileStateCache.GetFileState(moveAction.FromPath);

					if (fileState == null)
						throw new Exception($"Consistency error: The remote file state cache does not have an entry for the 'from' path {moveAction.FromPath}. Maybe you just need a check action for the path?");

					if (_parameters.IsExcludedPath(moveAction.ToPath))
					{
						VerboseWriteLine("[BQ] Move target is an excluded path, converting to DeleteAction");

						// Convert to a DeleteAction if we can't see the file any more.
						var deleteAction = new DeleteAction(moveAction.FromPath);

						ProcessBackupQueueAction(deleteAction);
					}
					else
					{
						VerboseWriteLine("[BQ] Registering file move in storage");

						_storage.MoveFile(
							PlaceInContentPath(moveAction.FromPath),
							PlaceInContentPath(moveAction.ToPath),
							_backupQueueCancellationTokenSource?.Token ?? CancellationToken.None);

						VerboseWriteLine("[BQ] Registering file move in Remote File State Cache");

						_remoteFileStateCache.RemoveFileState(moveAction.FromPath);
						_remoteFileStateCache.UpdateFileState(moveAction.ToPath, fileState);
					}

					break;
				case DeleteAction deleteAction:
					VerboseWriteLine("[BQ] => DeleteAction:");
					VerboseWriteLine("[BQ]   * Path: {0}", deleteAction.Path);
					VerboseWriteLine("[BQ] Removing from Remote File State Cache");

					if (!_remoteFileStateCache.RemoveFileState(deleteAction.Path))
						VerboseWriteLine("[BQ] Nothing to do, file was already not in the Remote File State Cache");
					else
					{
						VerboseWriteLine("[BQ] Deleting file from storage");

						_storage.DeleteFile(
							PlaceInContentPath(deleteAction.Path),
							_backupQueueCancellationTokenSource?.Token ?? CancellationToken.None);
					}

					break;
			}
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
				new Thread(() => UploadThreadProc(_cancelUploadsCancellationTokenSource.Token)) { Name = "Upload Thread #" + i }.Start();
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
						VerboseWriteLine("[UP] Uploading: {0}", fileToUpload.Path);

						_storage.UploadFile(PlaceInContentPath(fileToUpload.Path), fileToUpload.Stream, cancellationToken);

						VerboseWriteLine("[UP] Registering file state change");
						_remoteFileStateCache.UpdateFileState(
							fileToUpload.Path,
							new FileState()
							{
								FileSize = fileToUpload.Stream.Length,
								LastModifiedUTC = fileToUpload.LastModifiedUTC,
								Checksum = fileToUpload.Checksum,
							});
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

			NonQuietWriteLine("Indexing ZFS volumes");

			_zfsInstanceByMountPoint.Clear();

			foreach (var volume in _zfs.EnumerateVolumes())
			{
				NonQuietWriteLine("* {0}", volume.MountPoint);

				var volumeZFS = new ZFS(_parameters, volume);

				_zfsInstanceByMountPoint.Add((volume.MountPoint!, volumeZFS));
			}

			_zfsInstanceByMountPoint.Sort((left, right) => right.MountPoint.Length - left.MountPoint.Length);

			using (var output = new DiagnosticOutputHook(_surfaceArea, NonQuietWriteLine))
			{
				output.WriteLine("Building surface area");
				_surfaceArea.BuildDefault();
			}

			if (_parameters.EnableFileAccessNotify)
			{
				using (var output = new DiagnosticOutputHook(_monitor, NonQuietWriteLine))
				{
					output.WriteLine("Starting file system monitor");
					_monitor.Start();
				}
			}

			NonQuietWriteLine("Authenticating with remote storage");
			_storage.Authenticate();

			NonQuietWriteLine("Starting worker threads:");
			NonQuietWriteLine("=> Open files poller");
			StartPollOpenFilesThread();
			NonQuietWriteLine("=> Long poller");
			StartLongPollingThread();
			NonQuietWriteLine("=> Backup queue processor");
			StartProcessBackupQueueThread();
			NonQuietWriteLine("=> Uploader");
			StartUploadThreads();
		}

		public void Stop()
		{
			_stopping = true;

			NonQuietWriteLine("Stopping file system monitor (if running)");
			_monitor.Stop();

			NonQuietWriteLine("Stopping remote file state cache");
			_remoteFileStateCache.Stop();

			NonQuietWriteLine("Waking threads so they can exit:");
			NonQuietWriteLine("=> Open files poller");
			WakePollOpenFilesThread();
			NonQuietWriteLine("=> Long poller");
			WakeLongPollingThread();
			NonQuietWriteLine("=> Backup queue processor");
			WakeProcessBackupQueueThread();

			if (!WaitForProcessBackupQueueThreadToExit(TimeSpan.FromSeconds(3)))
			{
				InterruptProcessBackupQueueThread();
				WaitForProcessBackupQueueThreadToExit(TimeSpan.FromSeconds(1));
			}

			// We don't need to sync to the open file handles polling thread exiting because it doesn't take actions.

			NonQuietWriteLine("=> Uploader");
			InterruptUploadThreads();

			WaitForUploadThreadsToExit();

			NonQuietWriteLine("Flushing remote file state cache");

			_remoteFileStateCache.WaitWhileBusy();
			_remoteFileStateCache.UploadCurrentBatchAndBeginNext();

			NonQuietWriteLine("Cleaning up resources");

			CleanUpLongPollingThread();
			CleanUpProcessBackupQueueThread();
			CleanUpUploadThreads();
		}
	}
}

