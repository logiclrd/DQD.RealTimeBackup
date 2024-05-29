using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
	public class BackupAgent : DiagnosticOutputBase, IBackupAgent
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

		static internal string PlaceInContentPath(string path)
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

		void GetQueueSizes_PendingIntake(BackupAgentQueueSizes queueSizes)
		{
			lock (_snapshotSharingDelaySync)
				queueSizes.NumberOfFilesPendingIntake = _snapshotSharingBatch.Count;
		}

		void GetQueueSizes_OpenFiles(BackupAgentQueueSizes queueSizes)
		{
			lock (_openFilesSync)
				queueSizes.NumberOfFilesPollingOpenHandles = _openFiles.Count;
		}

		void GetQueueSizes_LongPolling(BackupAgentQueueSizes queueSizes)
		{
			lock (_longPollingSync)
				queueSizes.NumberOfFilesPollingContentChanges = _longPollingQueue.Count;
		}

		void GetQueueSizes_BackupQueue(BackupAgentQueueSizes queueSizes)
		{
			lock (_backupQueueSync)
				queueSizes.NumberOfBackupQueueActions = _backupQueue.Count;
		}

		void GetQueueSizes_UploadQueue(BackupAgentQueueSizes queueSizes)
		{
			lock (_uploadQueueSync)
			{
				queueSizes.NumberOfQueuedUploads = _uploadQueue.Count;

				if (_uploadThreadStatus != null)
					queueSizes.UploadThreads = GetUploadThreads();
			}
		}

		public BackupAgentQueueSizes GetQueueSizes()
		{
			var queueSizes = new BackupAgentQueueSizes();

			GetQueueSizes_PendingIntake(queueSizes);
			GetQueueSizes_OpenFiles(queueSizes);
			GetQueueSizes_LongPolling(queueSizes);
			GetQueueSizes_BackupQueue(queueSizes);
			GetQueueSizes_UploadQueue(queueSizes);

			return queueSizes;
		}

		public UploadStatus?[] GetUploadThreads()
		{
			lock (_uploadQueueSync)
			{
				if (_uploadThreadStatus == null)
					return Array.Empty<UploadStatus>();
				else
				{
					var ret = new UploadStatus[_uploadThreadCount];

					Array.Copy(_uploadThreadStatus, ret, _uploadThreadCount);

					return ret;
				}
			}
		}

		public int CheckPath(string path)
		{
			if (File.Exists(path))
				BeginQueuePathForOpenFilesCheck(path);
			else if (_remoteFileStateCache.ContainsPath(path))
				AddActionToBackupQueue(new DeleteAction(path));

			return OpenFilesCount;
		}

		public int CheckPaths(IEnumerable<string> paths)
		{
			BeginQueuePathsForOpenFilesCheckAndCheckForDeletions(paths);

			return OpenFilesCount;
		}

		public int OpenFilesCount
		{
			get
			{
				lock (_openFilesSync)
					return _openFiles.Count;
			}
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

			try
			{
				if (!File.Exists(path) || (File.ResolveLinkTarget(path, returnFinalTarget: false) != null))
					return;
			}
			catch (FileNotFoundException)
			{
				return;
			}

			lock (_snapshotSharingDelaySync)
			{
				if (_snapshotSharingDelay == null)
				{
					VerboseDiagnosticOutput("[IN] Setting timer to end the queue path operation");
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

					try
					{
						if (File.Exists(path) && (File.ResolveLinkTarget(path, returnFinalTarget: false) == null))
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
					catch (FileNotFoundException) { }
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
				VerboseDiagnosticOutput("[IN] End queue path operation, collecting batch");

				_snapshotSharingDelay?.Dispose();
				_snapshotSharingDelay = null;

				var snapshots = new Dictionary<IZFS, SnapshotReferenceTracker>();

				lock (_openFilesSync)
				foreach (string path in _snapshotSharingBatch)
				{
					VerboseDiagnosticOutput("[IN] => {0}", path);

					var zfs = FindZFSVolumeForPath(path);

					if (zfs == null)
					{
						Console.Error.WriteLine("[IN] Can't process file because it doesn't appear to be on a ZFS volume: {0}", path);
						continue;
					}

					if (!snapshots.TryGetValue(zfs, out var snapshotReferenceTracker))
					{
						VerboseDiagnosticOutput("[IN]   * ZFS snapshot needed on volume {0}", zfs.MountPoint);

						// Temporary release the lock while creating the snapshot.
						Monitor.Exit(_openFilesSync);

						try
						{
							var snapshot = zfs.CreateSnapshot("RTB-" + DateTime.UtcNow.Ticks);

							snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot);

							snapshots[zfs] = snapshotReferenceTracker;
						}
						finally
						{
							Monitor.Enter(_openFilesSync);
						}
					}

					VerboseDiagnosticOutput("[IN]   * Queuing path for open files check");

					QueuePathForOpenFilesCheck(snapshotReferenceTracker.AddReference(path));

					if (_stopping)
						break;
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

				VerboseDiagnosticOutput("[->OF] Path queued with timeout {0}", withTimeout.TimeoutUTC);

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
			VerboseDiagnosticOutput("[OF] Thread started");

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
						VerboseDiagnosticOutput("[OF] Going to sleep");

						Monitor.Wait(_openFilesSync);

						VerboseDiagnosticOutput("[OF] Woken up");

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
						VerboseDiagnosticOutput("[OF] Waiting until next check: {0}", _parameters.OpenFileHandlePollingInterval);
						Monitor.Wait(_openFileHandlePollingSync, _parameters.OpenFileHandlePollingInterval);
					}

					if (_stopping)
						return;

					openFilesCached.Clear();

					lock (_openFilesSync)
						openFilesCached.AddRange(_openFiles);
				}

				VerboseDiagnosticOutput("[OF] Collecting open file handles");

				var openWriteFileHandleSet = _openFileHandles.EnumerateAll()
					.Where(handle => handle.FileAccess.HasFlag(FileAccess.Write))
					.Select(handle => handle.FileName)
					.ToHashSet();

				VerboseDiagnosticOutput("[OF] Inspecting {0} file(s)", openFilesCached.Count);

				var deadlineUTC = DateTime.UtcNow.AddSeconds(5);

				for (int i = openFilesCached.Count - 1; i >= 0; i--)
				{
					var fileReference = openFilesCached[i];

					if (!openWriteFileHandleSet.Contains(fileReference.SnapshotReference.Path))
					{
						VerboseDiagnosticOutput("[OF] => Ready: {0}", fileReference.SnapshotReference.Path);

						filesToPromote.Add(fileReference);
						openFilesCached.RemoveAt(i);

						if (DateTime.UtcNow >= deadlineUTC)
						{
							VerboseDiagnosticOutput("[OF] Passing {0} files off to the backup queue", filesToPromote.Count);
							break;
						}
					}

					if (_stopping)
						break;
				}

				if (filesToPromote.Any())
				{
					VerboseDiagnosticOutput("[OF] Promoting {0} file(s)", filesToPromote.Count);

					lock (_openFilesSync)
					{
						// _openFiles is just a List<>, making a naive implementation of this O(n^2). This is fine until an initial backup dumps 700,000 files on us all at once. Then,
						// this statement begins to take rather a long time to complete:
						//
						// foreach (var reference in filesToPromote)
						//   _openFiles.Remove(reference);
						//
						// In addition, we are typically removing a bunch of files from the start of the list but may have literally hundreds of thousands at the end. Repeatedly
						// removing one element at a time means copying those hundreds of thousands of keepers over and over. So, instead, we compact the list intelligently, by
						// maintaining a pointer to the insertion point of the last element we want to keep, and then as we walk through, when we find further keepers, we copy
						// them directly to their final position, and when we find ones to remove, we simply skip over them. When we get to the end, we now have a bunch of
						// garbage data (redundant references and references that should be removed) after that final index, but we can simply truncate the list to length.
						var filesToPromoteSet = filesToPromote.ToHashSet();

						int keepIndex = 0;

						for (int i = 0; i < _openFiles.Count; i++)
						{
							if (!filesToPromoteSet.Contains(_openFiles[i]))
							{
								_openFiles[keepIndex] = _openFiles[i];
								keepIndex++;
							}
						}

						if (keepIndex < _openFiles.Count)
							_openFiles.RemoveRange(keepIndex, _openFiles.Count - keepIndex);
					}

					AddActionsToBackupQueue(filesToPromote
						.Select(reference => reference.SnapshotReference)
						.Select(reference => new UploadAction(reference, reference.Path)));

					lock (_backupQueueSync)
					{
						if (_backupQueue.Count >= _parameters.QueueHighWaterMark)
						{
							VerboseDiagnosticOutput("[OF] Throttle");

							while (_backupQueue.Count >= _parameters.QueueLowWaterMark)
								Monitor.Wait(_backupQueueSync, TimeSpan.FromSeconds(10));

							VerboseDiagnosticOutput("[OF] Resuming");
						}
					}

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
			// Make sure the file didn't get deleted between us picking it up and creating the initial snapshot.
			if (File.Exists(snapshotReference.SnapshottedPath))
			{
				lock (_longPollingSync)
				{
					_longPollingQueue.Add(new LongPollingItem(snapshotReference, _parameters.MaximumLongPollingTime));
					Monitor.PulseAll(_longPollingSync);
				}
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

					var newZFSSnapshots = new Dictionary<IZFS, SnapshotReferenceTracker>();

					for (int i = _longPollingQueue.Count - 1; i >= 0; i--)
					{
						var item = _longPollingQueue[i];

						var zfs = FindZFSVolumeForPath(item.CurrentSnapshotReference.Path);

						if (zfs == null)
						{
							Console.Error.WriteLine("[LP] Can't process file because it doesn't appear to be on a ZFS volume: {0}", item.CurrentSnapshotReference.Path);
							_longPollingQueue.RemoveAt(i);
							continue;
						}

						if (!newZFSSnapshots.TryGetValue(zfs, out var snapshotReferenceTracker))
						{
							VerboseDiagnosticOutput("[LP] ZFS snapshot needed on volume {0}", zfs.MountPoint);

							var snapshot = zfs.CreateSnapshot("RTB-" + DateTime.UtcNow.Ticks);

							snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot);

							newZFSSnapshots[zfs] = snapshotReferenceTracker;
						}

						var newSnapshotReference = snapshotReferenceTracker.AddReference(item.CurrentSnapshotReference.Path);

						bool promoteFile = false;

						if ((item.DeadlineUTC < now)
						 || !_openFileHandles.Enumerate(newSnapshotReference.Path).Any(handle => handle.FileAccess.HasFlag(FileAccess.Write)))
						{
							item.CurrentSnapshotReference.Dispose();
							promoteFile = true;
						}
						else
						{
							if (!File.Exists(newSnapshotReference.SnapshottedPath))
							{
								// Looks like the file got deleted.
								_longPollingQueue.RemoveAt(i);
								continue;
							}

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
		LinkedList<BackupAction> _backupQueue = new LinkedList<BackupAction>();
		CancellationTokenSource? _backupQueueCancellationTokenSource;
		ManualResetEvent? _backupQueueExited;

		void AddActionToBackupQueue(BackupAction action)
		{
			lock (_backupQueueSync)
			{
				VerboseDiagnosticOutput("[BQ] Queuing {0}", action.GetType().Name);

				_backupQueue.AddLast(action);
				Monitor.PulseAll(_backupQueueSync);
			}
		}

		void AddActionsToBackupQueue(IEnumerable<BackupAction> actions)
		{
			lock (_backupQueueSync)
			{
				VerboseDiagnosticOutput("[BQ] Queuing {0} actions", actions.Count());

				foreach (var action in actions)
					_backupQueue.AddLast(action);

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

		bool _pauseQueuingUploads = false;

		void ProcessBackupQueueThreadProc()
		{
			try
			{
				while (!_stopping)
				{
					BackupAction? backupAction = null;

					lock (_backupQueueSync)
					{
						while (_backupQueue.Count == 0)
						{
							if (_stopping)
								return;

							VerboseDiagnosticOutput("[BQ] Going to sleep");

							Monitor.Wait(_backupQueueSync);

							VerboseDiagnosticOutput("[BQ] Woken up");
						}

						if (_stopping)
							return;

						if (_pauseQueuingUploads == false)
						{
							backupAction = _backupQueue.First!.Value;
							_backupQueue.RemoveFirst();
						}
						else
						{
							var listNode = _backupQueue.First;

							while (listNode != null)
							{
								if (!(listNode.Value is UploadAction))
								{
									backupAction = listNode.Value;
									_backupQueue.Remove(listNode);
									break;
								}

								listNode = listNode.Next;
							}

							if (backupAction == null)
							{
								lock (_uploadQueueSync)
								{
									if (_uploadQueue.Count < _parameters.QueueLowWaterMark)
									{
										VerboseDiagnosticOutput("[BQ] Resuming");
										_pauseQueuingUploads = false;
										continue;
									}
								}

								VerboseDiagnosticOutput("[BQ] Don't have any non-Upload actions, going to sleep");

								Monitor.Wait(_backupQueueSync);

								VerboseDiagnosticOutput("[BQ] Woken up");
							}
						}

						// Unthrottle.
						if (_backupQueue.Count == _parameters.QueueLowWaterMark - 1)
							Monitor.PulseAll(_backupQueueSync);
					}

					if (backupAction != null)
					{
						VerboseDiagnosticOutput("[BQ] Dispatching: {0}", backupAction);

						ProcessBackupQueueAction(backupAction);
					}
				}
			}
			finally
			{
				_backupQueueExited?.Set();
			}
		}

		internal void ProcessBackupQueueAction(BackupAction action)
		{
			VerboseDiagnosticOutput("[BQ] Beginning processing of backup action");

			switch (action)
			{
				case UploadAction uploadAction:
					VerboseDiagnosticOutput("[BQ] => UploadAction");
					VerboseDiagnosticOutput("[BQ]   * Path: {0}", uploadAction.ToPath);
					VerboseDiagnosticOutput("[BQ]   * Snapshotted Path: {0}", uploadAction.Source.SnapshottedPath);

					FileReference fileReference;

					Stream stream;

					try
					{
						stream = File.OpenRead(uploadAction.Source.SnapshottedPath);
					}
					catch (Exception e)
					{
						NonQuietDiagnosticOutput("Error opening file: {0}", uploadAction.Source.SnapshottedPath);
						NonQuietDiagnosticOutput("=> {0}: {1}", e.GetType().Name, e.Message);
						NonQuietDiagnosticOutput("This is a snapshot of: {0}", uploadAction.Source.Path);
						NonQuietDiagnosticOutput("This should not have happened. Aborting this file upload and returning the file to the intake queue.");

						BeginQueuePathForOpenFilesCheck(uploadAction.Source.Path);

						break;
					}

					var backedUpFileState = _remoteFileStateCache.GetFileState(uploadAction.Source.Path);
					var currentLocalFileChecksum = _checksum.ComputeChecksum(stream);

					if ((backedUpFileState != null) && (currentLocalFileChecksum == backedUpFileState.Checksum))
					{
						VerboseDiagnosticOutput("[BQ] Remote File State Cache says this exact file is already uploaded, releasing & skipping");
						uploadAction.Source.Dispose();
					}
					else
					{
						var currentLastModifiedUTC = File.GetLastWriteTimeUtc(uploadAction.Source.Path);

						if (stream.Length > _parameters.MaximumFileSizeForStagingCopy)
						{
							VerboseDiagnosticOutput("[BQ] Large file, uploading from snapshot");
							fileReference = new FileReference(uploadAction.Source, currentLastModifiedUTC, currentLocalFileChecksum);
						}
						else
						{
							VerboseDiagnosticOutput("[BQ] Small file, staging file and releasing snapshot");

							var stagedCopy = _staging.StageFile(stream);

							stream.Dispose();

							fileReference = new FileReference(uploadAction.Source.Path, stagedCopy, currentLastModifiedUTC, currentLocalFileChecksum);

							uploadAction.Source.Dispose();
						}

						if (AddFileReferenceToUploadQueue(fileReference) >= _parameters.QueueHighWaterMark)
						{
							VerboseDiagnosticOutput("[BQ] Throttle");
							_pauseQueuingUploads = true;
						}
					}

					break;
				case MoveAction moveAction:
					VerboseDiagnosticOutput("[BQ] => MoveAction");
					VerboseDiagnosticOutput("[BQ]   * From Path: {0}", moveAction.FromPath);
					VerboseDiagnosticOutput("[BQ]   * To Path: {0}", moveAction.ToPath);

					var fileState = _remoteFileStateCache.GetFileState(moveAction.FromPath);

					if (fileState == null)
						throw new Exception($"Consistency error: The remote file state cache does not have an entry for the 'from' path {moveAction.FromPath}. Maybe you just need a check action for the path?");

					if (_parameters.IsExcludedPath(moveAction.ToPath))
					{
						VerboseDiagnosticOutput("[BQ] Move target is an excluded path, converting to DeleteAction");

						// Convert to a DeleteAction if we can't see the file any more.
						var deleteAction = new DeleteAction(moveAction.FromPath);

						ProcessBackupQueueAction(deleteAction);
					}
					else
					{
						VerboseDiagnosticOutput("[BQ] Registering file move in storage");

						try
						{
							_storage.MoveFile(
								PlaceInContentPath(moveAction.FromPath),
								PlaceInContentPath(moveAction.ToPath),
								_backupQueueCancellationTokenSource?.Token ?? CancellationToken.None);
						}
						catch (Exception ex)
						{
							OnDiagnosticOutput("Move task failed with exception: {0}: {1}", ex.GetType().Name, ex.Message);
							OnDiagnosticOutput("Sending file to the intake queue: {0}", moveAction.FromPath);
							BeginQueuePathForOpenFilesCheck(moveAction.FromPath);
							OnDiagnosticOutput("Sending file to the intake queue: {0}", moveAction.ToPath);
							BeginQueuePathForOpenFilesCheck(moveAction.ToPath);
							break;
						}

						VerboseDiagnosticOutput("[BQ] Registering file move in Remote File State Cache");

						_remoteFileStateCache.RemoveFileState(moveAction.FromPath);
						_remoteFileStateCache.UpdateFileState(moveAction.ToPath, fileState);
					}

					break;
				case DeleteAction deleteAction:
					VerboseDiagnosticOutput("[BQ] => DeleteAction:");
					VerboseDiagnosticOutput("[BQ]   * Path: {0}", deleteAction.Path);
					VerboseDiagnosticOutput("[BQ] Removing from Remote File State Cache");

					if (!_remoteFileStateCache.RemoveFileState(deleteAction.Path))
						VerboseDiagnosticOutput("[BQ] Nothing to do, file was already not in the Remote File State Cache");
					else
					{
						VerboseDiagnosticOutput("[BQ] Deleting file from storage");

						try
						{
							_storage.DeleteFile(
								PlaceInContentPath(deleteAction.Path),
								_backupQueueCancellationTokenSource?.Token ?? CancellationToken.None);
						}
						catch (Exception ex)
						{
							OnDiagnosticOutput("Delete task failed with exception: {0}: {1}", ex.GetType().Name, ex.Message);
							break;
						}

						_remoteFileStateCache.RemoveFileState(deleteAction.Path);
					}

					break;

				default:
					NonQuietDiagnosticOutput("[BQ] INTERNAL ERROR: Have an action we don't know what to do with. None of the type filters matched it.");
					if (action == null)
						NonQuietDiagnosticOutput("[BQ] Somehow, this action is null (snuck past the nullability checks?)");
					else
						NonQuietDiagnosticOutput("[BQ] Action type is: {0}", action.GetType().FullName);

					if (action is IDisposable disposableAction)
					{
						NonQuietDiagnosticOutput("[BQ] => Action is disposable, so disposing it...");
						disposableAction.Dispose();
					}

					break;
			}
		}

		object _uploadQueueSync = new object();
		CancellationTokenSource? _cancelUploadsCancellationTokenSource;
		List<FileReference> _uploadQueue = new List<FileReference>();
		int _uploadThreadCount;
		UploadStatus?[]? _uploadThreadStatus;
		Semaphore? _uploadThreadsExited;

		public int UploadThreadCount => _uploadThreadCount;

		void StartUploadThreads()
		{
			_uploadThreadCount = _parameters.UploadThreadCount;
			_uploadThreadsExited = new Semaphore(initialCount: 0, maximumCount: _uploadThreadCount);

			_cancelUploadsCancellationTokenSource = new CancellationTokenSource();

			_uploadThreadStatus = new UploadStatus[_parameters.UploadThreadCount];

			for (int i = 0; i < _parameters.UploadThreadCount; i++)
				new Thread(idx => UploadThreadProc((int)idx!, _cancelUploadsCancellationTokenSource.Token)) { Name = "Upload Thread #" + i }.Start(i);
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

		internal int AddFileReferenceToUploadQueue(FileReference fileReference)
		{
			lock (_uploadQueueSync)
			{
				_uploadQueue.Add(fileReference);
				Monitor.PulseAll(_uploadQueueSync);

				return _uploadQueue.Count;
			}
		}

		void UploadThreadProc(int threadIndex, CancellationToken cancellationToken)
		{
			VerboseDiagnosticOutput("[UP{0}] Thread starting", threadIndex);

			if (_uploadThreadStatus == null)
				throw new Exception("Internal error");

			var exited = _uploadThreadsExited;

			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					int uploadQueueSize;
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

						uploadQueueSize = _uploadQueue.Count;
					}

					if (_pauseQueuingUploads && (uploadQueueSize < _parameters.QueueLowWaterMark))
					{
						// Unthrottle.
						lock (_backupQueueSync)
							Monitor.PulseAll(_backupQueueSync);
					}

					using (fileToUpload)
					{
						VerboseDiagnosticOutput("[UP{0}] Uploading: {1}", threadIndex, fileToUpload.Path);
						VerboseDiagnosticOutput("[UP{0}] Source path: {1}", threadIndex, fileToUpload.SourcePath);

						try
						{
							using (var stream = File.OpenRead(fileToUpload.SourcePath))
							{
								fileToUpload.FileSize = stream.Length;

								_uploadThreadStatus[threadIndex] = new UploadStatus(fileToUpload.Path, fileToUpload.FileSize);

								_storage.UploadFile(
									PlaceInContentPath(fileToUpload.Path),
									stream,
									progress => _uploadThreadStatus[threadIndex]!.Progress = progress,
									cancellationToken);

								_uploadThreadStatus[threadIndex] = null;
							}
						}
						catch (Exception ex)
						{
							OnDiagnosticOutput("Upload task failed with exception: {0}: {1}", ex.GetType().Name, ex.Message);
							OnDiagnosticOutput("Returning file to the intake queue");
							BeginQueuePathForOpenFilesCheck(fileToUpload.Path);
							continue;
						}

						VerboseDiagnosticOutput("[UP{0}] Registering file state change", threadIndex);

						try
						{
							_remoteFileStateCache.UpdateFileState(
								fileToUpload.Path,
								new FileState()
								{
									FileSize = fileToUpload.FileSize,
									LastModifiedUTC = fileToUpload.LastModifiedUTC,
									Checksum = fileToUpload.Checksum,
								});
						}
						catch (TaskCanceledException)
						{
							Console.Error.WriteLine("A remote file state cache upload operation was cancelled. This may result in consistency errors.");
						}
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

			NonQuietDiagnosticOutput("Indexing ZFS volumes");

			_zfsInstanceByMountPoint.Clear();

			foreach (var volume in _zfs.EnumerateVolumes())
			{
				NonQuietDiagnosticOutput("* {0}", volume.MountPoint);

				if (!Directory.Exists(Path.Combine(volume.MountPoint!, ".zfs")))
				{
					NonQuietDiagnosticOutput("  => Not a real mount!");
					continue;
				}

				var volumeZFS = new ZFS(_parameters, volume, _zfs);

				_zfsInstanceByMountPoint.Add((volume.MountPoint!, volumeZFS));
			}

			_zfsInstanceByMountPoint.Sort((left, right) => right.MountPoint.Length - left.MountPoint.Length);

			using (var output = new DiagnosticOutputHook(_surfaceArea, NonQuietDiagnosticOutput))
			{
				output.WriteLine("Building surface area");
				_surfaceArea.BuildDefault();
			}

			if (_parameters.EnableFileAccessNotify)
			{
				using (var output = new DiagnosticOutputHook(_monitor, NonQuietDiagnosticOutput))
				{
					output.WriteLine("Starting file system monitor");
					_monitor.Start();
				}
			}

			NonQuietDiagnosticOutput("Starting remote file state cache");
			_remoteFileStateCache.Start();

			NonQuietDiagnosticOutput("Starting worker threads:");
			NonQuietDiagnosticOutput("=> Open files poller");
			StartPollOpenFilesThread();
			NonQuietDiagnosticOutput("=> Long poller");
			StartLongPollingThread();
			NonQuietDiagnosticOutput("=> Backup queue processor");
			StartProcessBackupQueueThread();
			NonQuietDiagnosticOutput("=> Uploader");
			StartUploadThreads();
		}

		public void Stop()
		{
			_stopping = true;

			NonQuietDiagnosticOutput("Stopping remote file state cache");
			_remoteFileStateCache.Stop();

			NonQuietDiagnosticOutput("Stopping file system monitor (if running)");
			_monitor.Stop();

			NonQuietDiagnosticOutput("Waking threads so they can exit:");
			NonQuietDiagnosticOutput("=> Open files poller");
			WakePollOpenFilesThread();
			NonQuietDiagnosticOutput("=> Long poller");
			WakeLongPollingThread();
			NonQuietDiagnosticOutput("=> Uploader");
			InterruptUploadThreads();
			NonQuietDiagnosticOutput("=> Backup queue processor");
			WakeProcessBackupQueueThread();

			if (!WaitForProcessBackupQueueThreadToExit(TimeSpan.FromSeconds(3)))
			{
				InterruptProcessBackupQueueThread();
				WaitForProcessBackupQueueThreadToExit(TimeSpan.FromSeconds(1));
			}

			// We don't need to sync to the open file handles polling thread exiting because it doesn't take actions.

			WaitForUploadThreadsToExit();

			NonQuietDiagnosticOutput("Flushing remote file state cache");

			_remoteFileStateCache.WaitWhileBusy();

			try
			{
				_remoteFileStateCache.UploadCurrentBatchAndBeginNext();
			}
			catch (TaskCanceledException)
			{
				Console.Error.WriteLine("The remote file state cache upload operation was cancelled. This may result in consistency errors.");
			}

			NonQuietDiagnosticOutput("Cleaning up resources");

			CleanUpLongPollingThread();
			CleanUpProcessBackupQueueThread();
			CleanUpUploadThreads();
		}
	}
}

