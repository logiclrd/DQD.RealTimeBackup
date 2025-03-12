using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using DQD.RealTimeBackup.ActivityMonitor;
using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.FileSystem;
using DQD.RealTimeBackup.Interop;
using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.SurfaceArea;
using DQD.RealTimeBackup.Utility;

using ITimer = DQD.RealTimeBackup.Utility.ITimer;

namespace DQD.RealTimeBackup.Agent
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

		IErrorLogger _errorLogger;
		ITimer _timer;
		Func<IChecksum> _checksumFactory;
		IChecksum _checksum;
		ISurfaceArea _surfaceArea;
		IFileSystemMonitor _monitor;
		IOpenFileHandles _openFileHandles;
		IZFS _zfs;
		IStaging _staging;
		IRemoteFileStateCache _remoteFileStateCache;
		IRemoteStorage _storage;

		List<(string MountPoint, IZFS ZFS)> _zfsInstanceByMountPoint; // Sorted by decreasing length of MountPoint

		public BackupAgent(OperatingParameters parameters, IErrorLogger errorLogger, ITimer timer, Func<IChecksum> checksumFactory, ISurfaceArea surfaceArea, IFileSystemMonitor monitor, IOpenFileHandles openFileHandles, IZFS zfs, IStaging staging, IRemoteFileStateCache remoteFileStateCache, IRemoteStorage storage)
		{
			_parameters = parameters;

			_errorLogger = errorLogger;
			_timer = timer;
			_checksumFactory = checksumFactory;
			_surfaceArea = surfaceArea;
			_monitor = monitor;
			_openFileHandles = openFileHandles;
			_zfs = zfs;
			_staging = staging;
			_remoteFileStateCache = remoteFileStateCache;
			_storage = storage;

			_checksum = _checksumFactory();

			_zfsInstanceByMountPoint = new List<(string MountPoint, IZFS ZFS)>();

			_monitor.PathUpdate += monitor_PathUpdate;
			_monitor.PathMove += monitor_PathMove;
			_monitor.PathDelete += monitor_PathDelete;

			NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
		}

		void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
		{
			if (!e.IsAvailable)
				PauseNetworkThreads();
			else
				UnpauseNetworkThreads();
		}

		public static string PlaceInContentPath(string path)
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

			if (_parameters.IsExcludedPath(move.PathFrom) && _parameters.IsExcludedPath(move.PathTo))
				VerboseDiagnosticOutput("Ignoring PathMove event on excluded paths: {0} => {1}", move.PathFrom, move.PathTo);
			else
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

			if (_parameters.IsExcludedPath(delete.Path))
				VerboseDiagnosticOutput("Ignoring PathDelete event on excluded path: {0}", delete.Path);
			else
				AddActionToBackupQueue(new DeleteAction(delete.Path));
		}

		public void PauseMonitor()
		{
			lock (_pauseSync)
			{
				_paused = true;
				_pathsWithActivityWhilePaused.Clear();

				NonQuietDiagnosticOutput("File System Monitor processing in the Backup Agent is paused.");
				NonQuietDiagnosticOutput("Events will be buffered until processing is resumed.");
			}
		}

		public void UnpauseMonitor(bool processBufferedPaths)
		{
			lock (_pauseSync)
			{
				_paused = false;

				try
				{
					NonQuietDiagnosticOutput("File System Monitor processing in the Backup Agent is resuming.");

					if (!processBufferedPaths)
						NonQuietDiagnosticOutput("=> Ignoring buffered events for {0} paths", _pathsWithActivityWhilePaused.Count);
					else
					{
						string[] capturedPaths = _pathsWithActivityWhilePaused.ToArray();

						NonQuietDiagnosticOutput("=> Processing {0} paths that had events while paused", capturedPaths.Length);

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
				finally
				{
					_pathsWithActivityWhilePaused.Clear();
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

		public void InspectQueues(Action<QueueType, IEnumerable<object>> inspector)
		{
			lock (_snapshotSharingDelaySync)
				inspector(QueueType.PendingIntake, _snapshotSharingBatch);

			lock (_openFilesSync)
				inspector(QueueType.OpenFiles, _openFiles);

			lock (_longPollingSync)
				inspector(QueueType.LongPolling, _longPollingQueue);

			lock (_backupQueueSync)
				inspector(QueueType.BackupQueue, _backupQueue);

			lock (_uploadQueueSync)
				inspector(QueueType.UploadQueue, _uploadQueue);
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
		HashSet<string> _snapshotSharingBatch = new HashSet<string>();

		void BeginQueuePathForOpenFilesCheck(string path)
		{
			if (path.IndexOf("/.zfs/snapshot/") >= 0)
				return;
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

		void CancelQueuePathForOpenFilesCheck()
		{
			var delay = _snapshotSharingDelay;

			_snapshotSharingDelay = null;

			if (delay != null)
				delay.Dispose();
		}

		void EndQueuePathForOpenFilesCheck()
		{
			lock (_snapshotSharingDelaySync)
			{
				_snapshotSharingDelay?.Dispose();
				_snapshotSharingDelay = null;

				if (_stopping)
				{
					VerboseDiagnosticOutput("[IN] End queue path operation cancelled, stopping");
					return;
				}

				VerboseDiagnosticOutput("[IN] End queue path operation, collecting batch");

				_snapshotSharingDelay?.Dispose();
				_snapshotSharingDelay = null;

				var snapshots = new Dictionary<IZFS, SnapshotReferenceTracker>();

				lock (_openFilesSync)
				{
					foreach (string path in _snapshotSharingBatch)
					{
						VerboseDiagnosticOutput("[IN] => {0}", path);

						var zfs = FindZFSVolumeForPath(path);

						if (zfs == null)
						{
							Console.Error.WriteLine("[IN] Can't process file because it doesn't appear to be on a ZFS volume: {0}", path);
							_errorLogger.LogError("The Backup Agent was asked to consider the following path:\n\n" + path + "\n\nIt does not appear to be on a ZFS volume.", ErrorLogger.Summary.InternalError);
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

								snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot, _errorLogger);

								snapshots[zfs] = snapshotReferenceTracker;
							}
							finally
							{
								Monitor.Enter(_openFilesSync);
							}
						}

						var snapshotReference = snapshotReferenceTracker.AddReference(path);

						if (!File.Exists(snapshotReference.SnapshottedPath))
						{
							VerboseDiagnosticOutput("[IN]   * File does not exist in the snapshot, it must have already been deleted. Removing from consideration.");
							snapshotReference.Dispose();
						}
						else
						{
							VerboseDiagnosticOutput("[IN]   * Queuing path for open files check");
							QueuePathForOpenFilesCheck(snapshotReference);
						}

						if (_stopping)
							break;
					}

					_snapshotSharingBatch.Clear();
				}
			}
		}

		object _openFilesSync = new object();
		List<SnapshotReferenceWithTimeout> _openFiles = new List<SnapshotReferenceWithTimeout>();

		internal void QueuePathForOpenFilesCheck(SnapshotReference reference)
		{
			VerboseDiagnosticOutput("[->OF] Queuing path for open files check: {0} ({1})", reference.Path, reference.SnapshottedPath);

			lock (_openFilesSync)
			{
				// Check if we're already polling this path. If the path has been resubmitted and the
				// filesize has changed, then we should reset the timeout window. We don't want to
				// repeatedly upload partial versions of a file that is actively in the process of
				// being created/downloaded. We only want to time out and upload files anyway if they
				// are either sitting stagnant or being modified in-place (e.g. a database).
				for (int i=0; i < _openFiles.Count; i++)
				{
					if (_openFiles[i].SnapshotReference.Path == reference.Path)
					{
						VerboseDiagnosticOutput("[->OF] => Path is already being polled for open files");

						// Reset the timeout if the file has grown.
						if (_openFiles[i].SnapshotReference.FileSize < reference.FileSize)
						{
							VerboseDiagnosticOutput("[->OF]    * File is growing, resetting timeout");
							_openFiles[i].TimeoutUTC = DateTime.UtcNow + _parameters.MaximumTimeToWaitForNoOpenFileHandles;
						}

						// Switch to the new snapshot.
						VerboseDiagnosticOutput("[->OF] => Switching to new snapshot: {0}", reference.SnapshottedPath);

						_openFiles[i].SnapshotReference.Dispose();
						_openFiles[i].SnapshotReference = reference;

						return;
					}
				}

				// Not found; add to the set.
				VerboseDiagnosticOutput("[->OF] => Path is not already being monitored, adding");

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

		void CleanUpPollOpenFilesThread()
		{
			lock (_openFilesSync)
			{
				_openFiles.ForEach(item => item.SnapshotReference.Dispose());
				_openFiles.Clear();
			}
		}

		void PollOpenFilesThreadProc()
		{
			VerboseDiagnosticOutput("[OF] Thread started");

			var openFilesCached = new List<SnapshotReferenceWithTimeout>();
			var filesToPromote = new List<SnapshotReferenceWithTimeout>();

			try
			{
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

							RunningState.Instance.PollOpenFilesThread.State = PollOpenFilesThreadState.Idle;

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
						RunningState.Instance.PollOpenFilesThread.State = PollOpenFilesThreadState.CollectingWait;

						var deadline = DateTime.UtcNow + _parameters.OpenFileHandlePollingInterval;

						while (DateTime.UtcNow < deadline)
						{
							lock (_openFileHandlePollingSync)
							{
								VerboseDiagnosticOutput("[OF] Waiting until next check: {0}", _parameters.OpenFileHandlePollingInterval);
								Monitor.Wait(_openFileHandlePollingSync, _parameters.OpenFileHandlePollingInterval);
							}

							if (_stopping)
								return;
						}

						openFilesCached.Clear();

						lock (_openFilesSync)
							openFilesCached.AddRange(_openFiles);
					}

					VerboseDiagnosticOutput("[OF] Collecting open file handles");

					RunningState.Instance.PollOpenFilesThread.State = PollOpenFilesThreadState.EnumeratingHandles;

					var openWriteFileHandleSet = _openFileHandles.EnumerateAll()
						.Where(handle => handle.FileAccess.HasFlag(FileAccess.Write))
						.Select(handle => handle.FileName)
						.ToHashSet();

					RunningState.Instance.PollOpenFilesThread.State = PollOpenFilesThreadState.InspectingFiles;

					VerboseDiagnosticOutput("[OF] Inspecting {0} file(s)", openFilesCached.Count);

					var deadlineUTC = DateTime.UtcNow.AddSeconds(5);

					for (int i = openFilesCached.Count - 1; i >= 0; i--)
					{
						var fileReference = openFilesCached[i];

						RunningState.Instance.PollOpenFilesThread.CurrentFile = fileReference.SnapshotReference;

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

					RunningState.Instance.PollOpenFilesThread.CurrentFile = null;

					if (filesToPromote.Any())
					{
						VerboseDiagnosticOutput("[OF] Promoting {0} file(s)", filesToPromote.Count);

						RunningState.Instance.PollOpenFilesThread.State = PollOpenFilesThreadState.PromotingFiles;

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
			catch (Exception e)
			{
				RunningState.Instance.PollOpenFilesThread.State = PollOpenFilesThreadState.Crashed;
				RunningState.Instance.PollOpenFilesThread.Exception = e;
			}
			finally
			{
				if (RunningState.Instance.PollOpenFilesThread.State != PollOpenFilesThreadState.Crashed)
					RunningState.Instance.PollOpenFilesThread.State = PollOpenFilesThreadState.Stopped;
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

			lock (_longPollingSync)
			{
				_longPollingQueue.ForEach(item => item.CurrentSnapshotReference.Dispose());
				_longPollingQueue.Clear();
			}
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
			VerboseDiagnosticOutput("[LP] Thread starting");

			DateTime intervalEndUTC = DateTime.UtcNow;

			List<LongPollingItem> queueCopy = new List<LongPollingItem>();

			try
			{
				while (!_stopping)
				{
					List<BackupAction>? actions = null;

					VerboseDiagnosticOutput("[LP] Obtaining sync");

					queueCopy.Clear();

					lock (_longPollingSync)
					{
						RunningState.Instance.LongPollingThread.State = LongPollingThreadState.Idle;

						if (_longPollingQueue.Count == 0)
						{
							VerboseDiagnosticOutput("[LP] => Queue is empty, waiting on sync");
							Monitor.Wait(_longPollingSync);
							continue;
						}

						var maximumDeadLineUTC = DateTime.UtcNow + _parameters.LongPollingInterval;

						VerboseDiagnosticOutput("[LP] Have {0} items for long polling, set deadline limit to {1}", _longPollingQueue.Count, maximumDeadLineUTC);

						if (_longPollingQueue.Count == 0)
							intervalEndUTC = maximumDeadLineUTC;
						else
						{
							intervalEndUTC = _longPollingQueue.Select(entry => entry.DeadlineUTC).Min();

							VerboseDiagnosticOutput("[LP] => latest deadline in queue: {0}", intervalEndUTC);

							if (intervalEndUTC > maximumDeadLineUTC)
							{
								VerboseDiagnosticOutput("[LP] => clamping to limit");
								intervalEndUTC = maximumDeadLineUTC;
							}
						}

						var waitDuration = intervalEndUTC - DateTime.UtcNow;

						while ((waitDuration > TimeSpan.Zero) && !_stopping)
						{
							VerboseDiagnosticOutput("[LP] Waiting for {0}", waitDuration);
							Monitor.Wait(_longPollingSync, waitDuration);
							waitDuration = intervalEndUTC - DateTime.UtcNow;
						}

						VerboseDiagnosticOutput("[LP] Wait loop finished");

						if (_stopping)
						{
							VerboseDiagnosticOutput("[LP] STOPPING");
							break;
						}

						queueCopy.AddRange(_longPollingQueue);

						VerboseDiagnosticOutput("[LP] Releasing sync, will process {0} items", queueCopy.Count);
					}

					var now = DateTime.UtcNow;

					var newZFSSnapshots = new Dictionary<IZFS, SnapshotReferenceTracker>();
					var removeFromQueue = new List<LongPollingItem>();

					RunningState.Instance.LongPollingThread.State = LongPollingThreadState.EnumeratingHandles;

					VerboseDiagnosticOutput("[LP] Collecting open file handles");

					var openWriteFileHandleSet = _openFileHandles.EnumerateAll()
						.Where(handle => handle.FileAccess.HasFlag(FileAccess.Write))
						.Select(handle => handle.FileName)
						.ToHashSet();

					RunningState.Instance.LongPollingThread.State = LongPollingThreadState.ProcessingItems;

					VerboseDiagnosticOutput("[LP] Processing {0} items", queueCopy.Count);

					foreach (var item in queueCopy)
					{
						RunningState.Instance.LongPollingThread.CurrentFile = item.CurrentSnapshotReference;

						VerboseDiagnosticOutput("[LP] - {0}", item.CurrentSnapshotReference.Path);

						var zfs = FindZFSVolumeForPath(item.CurrentSnapshotReference.Path);

						if (zfs == null)
						{
							Console.Error.WriteLine("[LP] Can't process file because it doesn't appear to be on a ZFS volume: {0}", item.CurrentSnapshotReference.Path);
							_errorLogger.LogError("The Backup Agent was asked to consider the path:\n\n" + item.CurrentSnapshotReference.Path + "\n\nThe Long Polling thread can't find the ZFS volume for this path.", ErrorLogger.Summary.InternalError);
							item.CurrentSnapshotReference.Dispose();
							removeFromQueue.Add(item);
							continue;
						}

						if (!newZFSSnapshots.TryGetValue(zfs, out var snapshotReferenceTracker))
						{
							VerboseDiagnosticOutput("[LP] ZFS snapshot needed on volume {0}", zfs.MountPoint);

							var snapshot = zfs.CreateSnapshot("RTB-" + DateTime.UtcNow.Ticks);

							snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot, _errorLogger);

							newZFSSnapshots[zfs] = snapshotReferenceTracker;
						}

						var newSnapshotReference = snapshotReferenceTracker.AddReference(item.CurrentSnapshotReference.Path);

						if (!File.Exists(newSnapshotReference.SnapshottedPath))
						{
							VerboseDiagnosticOutput("[LP]   File does not exist in new snapshot, removing from consideration");

							// Looks like the file got deleted.
							item.CurrentSnapshotReference.Dispose();
							newSnapshotReference.Dispose();
							removeFromQueue.Add(item);
							continue;
						}

						bool promoteFile = false;

						bool haveWriteHandles = openWriteFileHandleSet.Contains(newSnapshotReference.Path);

						if ((item.DeadlineUTC < now)
						|| !haveWriteHandles)
						{
							if (!haveWriteHandles)
								VerboseDiagnosticOutput("[LP]   File no longer has any writers, promoting");
							else
								VerboseDiagnosticOutput("[LP]   File has been busy for too long, promoting anyway");

							item.CurrentSnapshotReference.Dispose();
							item.CurrentSnapshotReference = newSnapshotReference;

							promoteFile = true;
						}
						else
						{
							VerboseDiagnosticOutput("[LP]   Comparing file content between old and new snapshots");
							VerboseDiagnosticOutput("[LP]   * {0}", item.CurrentSnapshotReference.SnapshottedPath);
							VerboseDiagnosticOutput("[LP]   * {0}", newSnapshotReference.SnapshottedPath);

							bool fileLooksStable;

							try
							{
								RunningState.Instance.LongPollingThread.ComparingFiles = true;

								fileLooksStable = FileUtility.FilesAreEqual(
									item.CurrentSnapshotReference.SnapshottedPath,
									newSnapshotReference.SnapshottedPath,
									_longPollingCancellationTokenSource?.Token ?? CancellationToken.None);
							}
							finally
							{
								RunningState.Instance.LongPollingThread.ComparingFiles = false;
							}

							VerboseDiagnosticOutput("[LP]   Switching to new snapshot, releasing old snapshot");

							item.CurrentSnapshotReference.Dispose();
							item.CurrentSnapshotReference = newSnapshotReference;

							if (fileLooksStable)
							{
								VerboseDiagnosticOutput("[LP]   File looks stable, promoting");
								promoteFile = true;
							}
						}

						if (promoteFile)
						{
							removeFromQueue.Add(item);

							if (actions == null)
								actions = new List<BackupAction>();

							actions.Add(new UploadAction(newSnapshotReference, newSnapshotReference.Path));
						}
					}

					if ((actions != null) && !_stopping)
					{
						RunningState.Instance.LongPollingThread.State = LongPollingThreadState.QueuingActions;

						VerboseDiagnosticOutput("[LP] Adding {0} actions to the backup queue", actions.Count);
						AddActionsToBackupQueue(actions);
					}

					if (removeFromQueue.Count > 0)
					{
						RunningState.Instance.LongPollingThread.State = LongPollingThreadState.TrimmingQueue;

						VerboseDiagnosticOutput("[LP] Removing {0} items from the long polling queue", removeFromQueue.Count);

						lock (_longPollingSync)
						{
							// If we ever end up with a large number of items in this list, we need to make sure we
							// use a performant method for removing items. We use the method of compaction, copying
							// items to be kept directly to their new indices.

							var removeFromQueueSet = removeFromQueue.ToHashSet();

							int keepIndex = 0;

							for (int i = 0; i < _longPollingQueue.Count; i++)
							{
								if (!removeFromQueueSet.Contains(_longPollingQueue[i]))
								{
									_longPollingQueue[keepIndex] = _longPollingQueue[i];
									keepIndex++;
								}
							}

							if (keepIndex < _longPollingQueue.Count)
								_longPollingQueue.RemoveRange(keepIndex, _longPollingQueue.Count - keepIndex);
						}
					}
				}
			}
			catch (Exception e)
			{
				RunningState.Instance.LongPollingThread.State = LongPollingThreadState.Crashed;
				RunningState.Instance.LongPollingThread.Exception = e;
			}
			finally
			{
				if (RunningState.Instance.LongPollingThread.State != LongPollingThreadState.Crashed)
					RunningState.Instance.LongPollingThread.State = LongPollingThreadState.Stopped;
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

		bool _networkThreadsPaused;

		void PauseNetworkThreads()
		{
			_networkThreadsPaused = true;
		}

		void UnpauseNetworkThreads()
		{
			_networkThreadsPaused = false;
			WakeProcessBackupQueueThread();
			WakeUploadThreads();
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

			lock (_backupQueueSync)
			{
				while (_backupQueue.Count > 0)
				{
					_backupQueue.First!.Value.Dispose();
					_backupQueue.RemoveFirst();
				}
			}
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
						while ((_backupQueue.Count == 0) || _networkThreadsPaused)
						{
							if (_networkThreadsPaused)
								RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.Paused;
							else
								RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.Idle;

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
						try
						{
							RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.ProcessingAction;
							RunningState.Instance.ProcessBackupQueueThread.Action = backupAction;

							VerboseDiagnosticOutput("[BQ] Dispatching: {0}", backupAction);

							ProcessBackupQueueAction(backupAction);
						}
						finally
						{
							RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.Idle;
							RunningState.Instance.ProcessBackupQueueThread.Path = null;
							RunningState.Instance.ProcessBackupQueueThread.FromPath = null;
						}
					}
				}
			}
			catch (Exception e)
			{
				RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.Crashed;
				RunningState.Instance.ProcessBackupQueueThread.Exception = e;
			}
			finally
			{
				if (RunningState.Instance.ProcessBackupQueueThread.State != ProcessBackupQueueThreadState.Crashed)
					RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.Stopped;

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
						RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.ProcessingAction_OpeningFile;
						RunningState.Instance.ProcessBackupQueueThread.Path = uploadAction.Source.SnapshottedPath;

						stream = File.OpenRead(uploadAction.Source.SnapshottedPath);
					}
					catch (Exception exception)
					{
						_errorLogger.LogError(
							"The Backup Agent queued an Upload Action with the following source path:\n" +
							"\n" +
							uploadAction.Source.SnapshottedPath + "\n" +
							"\n" +
							"This is a snapshot of: " + uploadAction.Source.Path + "\n" +
							"\n" +
							"The source path could not be opened in order to perform the upload. This should not have happened. The file upload " +
							"will be aborted and the file will be returned to the intake queue.",
							ErrorLogger.Summary.InternalError,
							exception);

						BeginQueuePathForOpenFilesCheck(uploadAction.Source.Path);

						uploadAction.Source.Dispose();

						break;
					}

					var backedUpFileState = _remoteFileStateCache.GetFileState(uploadAction.Source.Path);
					var currentLocalFileChecksum = _checksum.ComputeChecksum(stream);

					if ((backedUpFileState != null)
					 && !backedUpFileState.IsInParts // if the file is uploaded in parts, each part needs to be checked separately
					 && (currentLocalFileChecksum == backedUpFileState.Checksum))
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
							RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.ProcessingAction_StagingFile;

							VerboseDiagnosticOutput("[BQ] Small file, staging file and releasing snapshot");

							var stagedCopy = _staging.StageFile(stream);

							stream.Dispose();

							fileReference = new FileReference(uploadAction.Source.Path, stagedCopy, currentLastModifiedUTC, currentLocalFileChecksum);

							RunningState.Instance.ProcessBackupQueueThread.Path = stagedCopy.Path;

							uploadAction.Source.Dispose();
						}

						RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.ProcessingAction_QueuingUpload;

						if (AddFileReferenceToUploadQueue(fileReference) >= _parameters.QueueHighWaterMark)
						{
							VerboseDiagnosticOutput("[BQ] Throttle");
							_pauseQueuingUploads = true;
						}
					}

					break;
				case MoveAction moveAction:
				{
					var existingUploadOfPath =
						_uploadThreadStatus?.FirstOrDefault(status => (status != null) && (status.Path == moveAction.FromPath) && !status.IsCompleted) ??
						_uploadThreadStatus?.FirstOrDefault(status => (status != null) && (status.Path == moveAction.ToPath) && !status.IsCompleted);

					if (existingUploadOfPath != null)
					{
						NonQuietDiagnosticOutput("[BQ] => MoveAction: An upload thread is already uploading this path: {0}", existingUploadOfPath.Path);
						NonQuietDiagnosticOutput("[BQ] => MoveAction: Will reprocess move action after the upload completes");
						existingUploadOfPath.RecheckFunctor = () => ProcessBackupQueueAction(action);
						existingUploadOfPath.RecheckAfterUploadCompletes();
						break;
					}

					VerboseDiagnosticOutput("[BQ] => MoveAction");
					NonQuietDiagnosticOutput("[BQ] File moved locally, moving on server");
					NonQuietDiagnosticOutput("[BQ]   * From Path: {0}", moveAction.FromPath);
					NonQuietDiagnosticOutput("[BQ]   * To Path: {0}", moveAction.ToPath);

					var fileState = _remoteFileStateCache.GetFileState(moveAction.FromPath);

					if (fileState == null)
					{
						VerboseDiagnosticOutput("[BQ] From Path does not exist in the remote file state cache");
						VerboseDiagnosticOutput("[MQ] => We should double-check that we don't have a content file at that path and then process the To Path as a new file");

						var deleteAction = new DeleteAction(moveAction.FromPath);

						ProcessBackupQueueAction(deleteAction);
						BeginQueuePathForOpenFilesCheck(moveAction.ToPath);

						break;
					}

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
							RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.ProcessingAction_MovingFile;
							RunningState.Instance.ProcessBackupQueueThread.Path = moveAction.ToPath;
							RunningState.Instance.ProcessBackupQueueThread.FromPath = moveAction.FromPath;

							_storage.MoveFile(
								PlaceInContentPath(moveAction.FromPath),
								PlaceInContentPath(moveAction.ToPath),
								_backupQueueCancellationTokenSource?.Token ?? CancellationToken.None);
						}
						catch (Exception ex)
						{
							OnDiagnosticOutput("Move task failed with exception: {0}: {1}", ex.GetType().Name, ex.Message);

							if (_networkThreadsPaused)
							{
								OnDiagnosticOutput("Network threads are now paused, re-queuing action");
								AddActionToBackupQueue(moveAction);
							}
							else
							{
								OnDiagnosticOutput("Sending file to the intake queue: {0}", moveAction.FromPath);
								BeginQueuePathForOpenFilesCheck(moveAction.FromPath);
								OnDiagnosticOutput("Sending file to the intake queue: {0}", moveAction.ToPath);
								BeginQueuePathForOpenFilesCheck(moveAction.ToPath);
							}

							break;
						}

						RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.ProcessingAction_RegisteringFileMove;

						VerboseDiagnosticOutput("[BQ] Registering file move in Remote File State Cache");

						_remoteFileStateCache.RemoveFileState(moveAction.FromPath);
						_remoteFileStateCache.UpdateFileState(moveAction.ToPath, fileState);
					}

					break;
				}
				case DeleteAction deleteAction:
				{
					if (_uploadThreadStatus != null)
					{
						int existingUploadOfPathIndex = Array.FindIndex(
							_uploadThreadStatus,
							status => (status != null) && (status.Path == deleteAction.Path) && !status.IsCompleted);

						if (existingUploadOfPathIndex >= 0)
						{
							NonQuietDiagnosticOutput("[BQ] => DeleteAction: Cancelling an ongoing upload of this path: {0}", deleteAction.Path);
							_uploadThreadStatus[existingUploadOfPathIndex]?.RecheckAfterUploadCompletes();
							CancelUpload(existingUploadOfPathIndex);
							break;
						}
					}

					RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.ProcessingAction_RegisteringFileDeletion;
					RunningState.Instance.ProcessBackupQueueThread.Path = deleteAction.Path;

					VerboseDiagnosticOutput("[BQ] => DeleteAction:");
					NonQuietDiagnosticOutput("[BQ] File deleted locally, deleting from server: {0}", deleteAction.Path);
					VerboseDiagnosticOutput("[BQ] Removing from Remote File State Cache");

					bool expectContentFileToExist = _remoteFileStateCache.RemoveFileState(deleteAction.Path);

					if (expectContentFileToExist)
						VerboseDiagnosticOutput("[BQ] Deleting file from storage");
					else
						VerboseDiagnosticOutput("[BQ] File was already not in the Remote File State Cache, double-check that there's no corresponding content file");

					try
					{
						RunningState.Instance.ProcessBackupQueueThread.State = ProcessBackupQueueThreadState.ProcessingAction_DeletingFile;
						_storage.DeleteFile(
							PlaceInContentPath(deleteAction.Path),
							_backupQueueCancellationTokenSource?.Token ?? CancellationToken.None);
					}
					catch (Exception ex)
					{
						if (_networkThreadsPaused)
						{
							OnDiagnosticOutput("Network threads are now paused, re-queuing action");
							AddActionToBackupQueue(deleteAction);
						}
						else
						{
							if (expectContentFileToExist)
							{
								if (IsTransientError(ex))
								{
									OnDiagnosticOutput("Encountered a transient error deleting a file, re-queuing action");
									AddActionToBackupQueue(deleteAction);
								}
								else
									_errorLogger.LogError("Failed to delete a content file that was expected to be present in remote storage: " + deleteAction.Path, ErrorLogger.Summary.InternalError, ex);
							}
							else
								VerboseDiagnosticOutput("Delete task failed with exception: {0}: {1}", ex.GetType().Name, ex.Message);
						}
					}

					break;
				}

				default:
				{
					// This shouldn't ever happen.
					_errorLogger.LogError(
						"The Backup Queue has an action it doesn't know what to do with. None of the type filters matched it.\n" +
						"\n" +
						((action == null)
						? "Somehow, this action is null (snuck past the nullability checks?)"
						: ("Action type is: " + action.GetType().FullName)),
						"DQD.RealTimeBackup: Backup Queue Internal Error");

					if (action is IDisposable disposableAction)
					{
						NonQuietDiagnosticOutput("[BQ] => Action is disposable, so disposing it...");
						disposableAction.Dispose();
					}

					break;
				}
			}
		}

		bool IsTransientError(Exception ex)
		{
			if (ex is SocketException)
				return true;
			if (ex is TaskCanceledException)
				return true;
			if (ex is NullResponseException)
				return true;

			if ((ex is HttpRequestException httpException) && httpException.StatusCode.HasValue)
				return (httpException.StatusCode < HttpStatusCode.InternalServerError);

			if (ex is AggregateException aggregate)
				return aggregate.InnerExceptions.Any(IsTransientError);

			if (ex.InnerException is Exception inner)
				return IsTransientError(inner);

			return false;
		}

		object _uploadQueueSync = new object();
		CancellationTokenSource? _cancelUploadsCancellationTokenSource;
		List<FileReference> _uploadQueue = new List<FileReference>();
		int _uploadThreadCount;
		UploadStatus?[]? _uploadThreadStatus;
		CancellationTokenSource?[]? _uploadThreadCancellation;
		Semaphore? _uploadThreadsExited;

		public int UploadThreadCount => _uploadThreadCount;

		void StartUploadThreads()
		{
			_uploadThreadCount = _parameters.UploadThreadCount;
			_uploadThreadsExited = new Semaphore(initialCount: 0, maximumCount: _uploadThreadCount);

			_cancelUploadsCancellationTokenSource = new CancellationTokenSource();
			_uploadThreadCancellation = new CancellationTokenSource[_uploadThreadCount];

			_uploadThreadStatus = new UploadStatus[_parameters.UploadThreadCount];

			for (int i = 0; i < _parameters.UploadThreadCount; i++)
			{
				new Thread(idx => UploadThreadProc((int)idx!, _cancelUploadsCancellationTokenSource.Token)) { Name = "Upload Thread #" + i }.Start(i);
			}
		}

		void WakeUploadThreads()
		{
			// Idle Upload threads wait on _uploadQueueSync.
			lock (_uploadQueueSync)
				Monitor.PulseAll(_uploadQueueSync);
		}

		void InterruptUploadThreads()
		{
			WakeUploadThreads();

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

			lock (_uploadQueueSync)
			{
				_uploadQueue.ForEach(action => action.Dispose());
				_uploadQueue.Clear();
			}
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
				// If we are already uploading an older version of this path, cancel that upload.
				if (_uploadThreadStatus != null)
				{
					for (int i = 0; i < _uploadThreadStatus.Length; i++)
					{
						if (_uploadThreadStatus[i]?.Path == fileReference.Path)
						{
							NonQuietDiagnosticOutput("[BQ] AddFileReferenceToUploadQueue: Cancelling existing upload for path: {0}", fileReference.Path);
							CancelUpload(i);
						}
					}
				}

				// Ensure we don't upload the same file multiple times.
				for (int i = 0; i < _uploadQueue.Count; i++)
				{
					var queueEntry = _uploadQueue[i];

					if (queueEntry.Path == fileReference.Path)
					{
						// Already set to upload this file. But, our source is probably a newer version.
						// So, let's replace the existing entry with the new one.
						_uploadQueue[i] = fileReference;

						queueEntry.Dispose();

						return _uploadQueue.Count;
					}
				}

				// Didn't already find the file in the upload queue.
				_uploadQueue.Add(fileReference);
				Monitor.Pulse(_uploadQueueSync);

				return _uploadQueue.Count;
			}
		}

		void UploadThreadProc(int threadIndex, CancellationToken cancellationToken)
		{
			// Instance per thread; the system MD5 class may not be threadsafe.
			var checksum = _checksumFactory();

			VerboseDiagnosticOutput("[UP{0}] Thread starting", threadIndex);

			if (_uploadThreadStatus == null)
			{
				// This shouldn't ever happen.
				_errorLogger.LogError("Was started without the _uploadThreadStatus array initialized", "DQD.RealTimeBackup: Upload Thread Internal Error");
				throw new Exception("Internal error");
			}

			if (_uploadThreadCancellation == null)
			{
				// This shouldn't ever happen.
				_errorLogger.LogError("Was started without the _uploadThreadCancellation array initialized", "DQD.RealTimeBackup: Upload Thread Internal Error");
				throw new Exception("Internal error");
			}

			var exited = _uploadThreadsExited;

			using (var runningState = RunningState.Instance.UploadThreads.Register(threadIndex))
			try
			{
				while (!cancellationToken.IsCancellationRequested && !_stopping)
				{
					int uploadQueueSize;
					FileReference fileToUpload;

					lock (_uploadQueueSync)
					{
						while ((_uploadQueue.Count == 0) || _networkThreadsPaused)
						{
							if (_networkThreadsPaused)
								runningState.State = UploadThreadState.Paused;
							else
								runningState.State = UploadThreadState.Idle;

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
						runningState.State = UploadThreadState.ProcessingUpload;
						runningState.File = fileToUpload;

						var existingUploadOfPath = _uploadThreadStatus.FirstOrDefault(status => (status != null) && (status.Path == fileToUpload.Path) && !status.IsCompleted);

						if (existingUploadOfPath != null)
						{
							NonQuietDiagnosticOutput("[UP{0}] Ignoring upload action because another thread is already uploading this path: {0}", fileToUpload.Path);
							existingUploadOfPath.RecheckAfterUploadCompletes();
							continue;
						}

						NonQuietDiagnosticOutput("[UP{0}] Uploading: {1}", threadIndex, fileToUpload.Path);
						VerboseDiagnosticOutput("[UP{0}] Source path: {1}", threadIndex, fileToUpload.SourcePath);

						VerboseDiagnosticOutput("[UP{0}] Building File State structure", threadIndex);

						var newFileState =
							new FileState()
							{
								FileSize = fileToUpload.FileSize,
								LastModifiedUTC = fileToUpload.LastModifiedUTC,
								Checksum = fileToUpload.Checksum,
							};

						try
						{
							runningState.State = UploadThreadState.ProcessingUpload_OpeningFile;

							using (var stream = File.OpenRead(fileToUpload.SourcePath))
							{
								fileToUpload.FileSize = stream.Length;

								_uploadThreadCancellation[threadIndex] = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

								var localCancellationToken = _uploadThreadCancellation[threadIndex]?.Token ?? cancellationToken;

								_uploadThreadStatus[threadIndex] = new UploadStatus(
									fileToUpload.Path,
									fileToUpload.FileSize,
									() =>
									{
										VerboseDiagnosticOutput("[UP{0}] (post-completion action) Returning file to the intake queue: {1}", threadIndex, fileToUpload.Path);

										BeginQueuePathForOpenFilesCheck(fileToUpload.Path);
									});

								int partCount = (int)((fileToUpload.FileSize + _parameters.FilePartSize - 1) / _parameters.FilePartSize);

								if (partCount == 1)
								{
									runningState.State = UploadThreadState.ProcessingUpload_UploadingEntireFile;

									try
									{
										runningState.PerformingUpload = true;

										_storage.UploadFile(
											PlaceInContentPath(fileToUpload.Path),
											stream,
											out newFileState.ContentKey,
											progress =>
											{
												progress.TotalBytes = fileToUpload.FileSize;
												_uploadThreadStatus[threadIndex]!.Progress = progress;
											},
											localCancellationToken);
									}
									finally
									{
										runningState.PerformingUpload = false;
									}
								}
								else
								{
									runningState.State = UploadThreadState.ProcessingUpload_UploadingFileInParts;

									var existingFileState = _remoteFileStateCache.GetFileState(fileToUpload.Path);

									if (existingFileState != null)
										newFileState.ContentKey = existingFileState.ContentKey;

									var partsOnServer = _remoteFileStateCache.EnumerateFileParts(fileToUpload.Path).ToList();

									var partByPartNumber = partsOnServer.ToDictionary(part => part.PartNumber);

									var partsToUpload = new List<int>();

									long totalBytesToUpload = 0;

									string contentPath = PlaceInContentPath(fileToUpload.Path);

									for (int partNumber = 1; partNumber <= partCount; partNumber++)
									{
										long partOffset = (partNumber - 1) * (long)_parameters.FilePartSize;
										int partLength = (int)Math.Min(fileToUpload.FileSize - partOffset, _parameters.FilePartSize);

										var partStream = new Substream(stream, partOffset, partLength);

										if (!partByPartNumber.TryGetValue(partNumber, out var partState)
										 || (partState.Checksum != checksum.ComputeChecksum(partStream)))
										{
											partsToUpload.Add(partNumber);
											totalBytesToUpload += partLength;

											try
											{
												runningState.PerformingDeletion = true;
												runningState.PartNumber = partNumber;

												_storage.DeleteFilePart(
													contentPath,
													partNumber,
													localCancellationToken);
											}
											catch (FileNotFoundException) { }
											finally
											{
												runningState.PerformingDeletion = false;
												runningState.PartNumber = 0;
											}
										}
										else
										{
											// Remove this part number, since it doesn't need to be uploaded, otherwise the
											// cleanup code after the upload loop will delete the part.
											partByPartNumber.Remove(partNumber);
										}
									}

									long totalBytesTransferred = 0;

									foreach (int partNumber in partsToUpload)
									{
										runningState.PartNumber = partNumber;

										long partOffset = (partNumber - 1) * (long)_parameters.FilePartSize;
										int partLength = (int)Math.Min(fileToUpload.FileSize - partOffset, _parameters.FilePartSize);

										var partStream = new Substream(stream, partOffset, partLength);

										VerboseDiagnosticOutput("[UP{0}] Uploading part {1} of file {2}", threadIndex, partNumber, fileToUpload.Path);

										try
										{
											runningState.PerformingUpload = true;

											_storage.UploadFilePart(
												contentPath,
												partStream,
												partNumber,
												newContentKey =>
												{
													if (newFileState.ContentKey != newContentKey)
													{
														VerboseDiagnosticOutput("[UP{0}] Received content key for file: {1}", threadIndex, newContentKey);
														VerboseDiagnosticOutput("[UP{0}] => {1}", threadIndex, newContentKey);

														try
														{
															newFileState.ContentKey = newContentKey;
															newFileState.IsInParts = true;

															_remoteFileStateCache.UpdateFileState(
																fileToUpload.Path,
																newFileState);
														}
														catch (TaskCanceledException exception)
														{
															_errorLogger.LogError("A remote file state cache upload operation was cancelled. This may result in consistency errors.", ErrorLogger.Summary.ImportantBackupError, exception);
															throw;
														}
													}
												},
												progress =>
												{
													progress.BytesTransferred += totalBytesTransferred;

													if (progress.BytesTransferred > totalBytesToUpload)
														totalBytesToUpload = progress.BytesTransferred;

													progress.TotalBytes = totalBytesToUpload;

													_uploadThreadStatus[threadIndex]!.Progress = progress;
												},
												localCancellationToken);
										}
										finally
										{
											runningState.PerformingUpload = false;
										}

										if (!partByPartNumber.TryGetValue(partNumber, out var partState))
											partState = newFileState.CreatePartState(partNumber);

										partStream.Position = 0;

										partState.Checksum = checksum.ComputeChecksum(partStream);

										_remoteFileStateCache.UpdateFileState(
											fileToUpload.Path,
											partState);

										partByPartNumber.Remove(partNumber);

										totalBytesTransferred += partLength;
									}

									runningState.State = UploadThreadState.ProcessingUpload_DeletingUnneededParts;

									foreach (var unneededPartNumber in partByPartNumber.Keys)
									{
										runningState.PartNumber = unneededPartNumber;

										VerboseDiagnosticOutput("[UP{0}] Deleting part {0} of file {1}", unneededPartNumber, fileToUpload.Path);

										try
										{
											runningState.PerformingDeletion = true;

											_storage.DeleteFilePart(
												contentPath,
												unneededPartNumber,
												localCancellationToken);
										}
										finally
										{
											runningState.PerformingDeletion = false;
										}

										_remoteFileStateCache.RemoveFileStateForPart(fileToUpload.Path, unneededPartNumber);
									}
								}

								_uploadThreadStatus[threadIndex]!.MarkCompleted();
							}
						}
						catch (TaskCanceledException)
						{
							OnDiagnosticOutput("Upload action was cancelled for path: {0}", fileToUpload.Path);

							// Trigger recheck if requested.
							_uploadThreadStatus[threadIndex]?.MarkCompleted();

							break;
						}
						catch (Exception exception)
						{
							if (!IsTransientError(exception))
								_errorLogger.LogError("Upload task failed, returning file to the intake queue\n\nPath: " + fileToUpload.Path, ErrorLogger.Summary.ImportantBackupError, exception);

							BeginQueuePathForOpenFilesCheck(fileToUpload.Path);

							continue;
						}
						finally
						{
							runningState.PartNumber = 0;

							_uploadThreadStatus[threadIndex] = null;

							try
							{
								_uploadThreadCancellation[threadIndex]?.Dispose();
							}
							catch {}

							_uploadThreadCancellation[threadIndex] = null;
						}

						runningState.State = UploadThreadState.ProcessingUpload_RegisteringFileStateChange;

						VerboseDiagnosticOutput("[UP{0}] Registering file state change", threadIndex);

						try
						{
							_remoteFileStateCache.UpdateFileState(
								fileToUpload.Path,
								newFileState);
						}
						catch (TaskCanceledException exception)
						{
							_errorLogger.LogError("A remote file state cache upload operation was cancelled. This may result in consistency errors.", ErrorLogger.Summary.ImportantBackupError, exception);
						}
					}
				}
			}
			catch (Exception e)
			{
				runningState.State = UploadThreadState.Crashed;
				runningState.Exception = e;

				throw;
			}
			finally
			{
				if (runningState.State != UploadThreadState.Crashed)
					runningState.State = UploadThreadState.Stopped;

				exited?.Release();
			}
		}

		public void CancelUpload(int uploadThreadIndex)
		{
			if (_uploadThreadCancellation == null)
				return;

			try
			{
				_uploadThreadCancellation[uploadThreadIndex]?.Cancel();
			}
			catch {}
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

				var volumeZFS = new ZFS(_parameters, _errorLogger, _timer, volume, _zfs);

				_zfsInstanceByMountPoint.Add((volume.MountPoint!, volumeZFS));
			}

			_zfsInstanceByMountPoint.Sort((left, right) => right.MountPoint.Length - left.MountPoint.Length);

			NonQuietDiagnosticOutput("Searching for stale ZFS snapshots");

			foreach (var snapshot in _zfs.EnumerateSnapshots())
			{
				if (snapshot.DeviceName != null)
				{
					NonQuietDiagnosticOutput("* {0}", snapshot.DeviceName);

					var parts = snapshot.DeviceName.Split('@', count: 2);

					string deviceName = parts.First();
					string snapshotName = parts.Last();

					if (snapshotName.StartsWith("RTB-") && long.TryParse(snapshotName.Substring(4), out var timestampValue))
					{
						var volumeZFS = _zfsInstanceByMountPoint.FirstOrDefault(item => item.ZFS.DeviceName == deviceName).ZFS;

						if (volumeZFS != null)
						{
							NonQuietDiagnosticOutput("  => This looks like one of ours");

							using (volumeZFS.AttachToSnapshot(snapshotName))
								NonQuietDiagnosticOutput("  => Requesting deletion");;
						}
					}
				}
			}

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

			NonQuietDiagnosticOutput("Cancelling pending queue-path operations");
			CancelQueuePathForOpenFilesCheck();

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
			catch (TaskCanceledException exception)
			{
				_errorLogger.LogError("The remote file state cache upload operation was cancelled. This may result in consistency errors.", ErrorLogger.Summary.ImportantBackupError, exception);
			}

			NonQuietDiagnosticOutput("Terminating uploads");

			_cancelUploadsCancellationTokenSource?.Cancel();

			NonQuietDiagnosticOutput("Cleaning up resources");

			CleanUpPollOpenFilesThread();
			CleanUpLongPollingThread();
			CleanUpProcessBackupQueueThread();
			CleanUpUploadThreads();
		}
	}
}

