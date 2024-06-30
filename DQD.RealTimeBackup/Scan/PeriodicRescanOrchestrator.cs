using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.FileSystem;
using DQD.RealTimeBackup.Interop;
using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.SurfaceArea;

namespace DQD.RealTimeBackup.Scan
{
	public class PeriodicRescanOrchestrator : Scanner, IPeriodicRescanOrchestrator
	{
		OperatingParameters _parameters;

		IBackupAgent _backupAgent;
		IRemoteFileStateCache _remoteFileStateCache;

		public PeriodicRescanOrchestrator(OperatingParameters parameters, ISurfaceArea surfaceArea, IBackupAgent backupAgent, IRemoteFileStateCache remoteFileStateCache, IZFS zfs, IStat stat)
			: base(surfaceArea, stat)
		{
			_parameters = parameters;

			_backupAgent = backupAgent;
			_remoteFileStateCache = remoteFileStateCache;
		}

		public void PerformPeriodicRescan(int rescanNumber, Action<RescanStatus> statusUpdateCallback, CancellationToken cancellationToken)
		{
			NonQuietDiagnosticOutput("[PR] Beginning periodic rescan");

			var deletedPaths = new HashSet<string>();

			deletedPaths.UnionWith(_remoteFileStateCache.EnumeratePaths());

			NonQuietDiagnosticOutput("[PR] => {0} path{1} currently being tracked", deletedPaths.Count, deletedPaths.Count == 1 ? "" : "s");

			RescanStatus rescanStatus = new RescanStatus();

			rescanStatus.RescanNumber = rescanNumber;
			rescanStatus.IsRunning = true;

			Action enqueued =
				() => Interlocked.Increment(ref rescanStatus.DirectoryQueueSize);
			Action dequeued =
				() => Interlocked.Decrement(ref rescanStatus.DirectoryQueueSize);
			Action fileDiscovered =
				() => Interlocked.Increment(ref rescanStatus.FilesDiscovered);
			Action mountProcessed =
				() => Interlocked.Increment(ref rescanStatus.MountsProcessed);

			DateTime nextStatusUpdateUTC = DateTime.UtcNow;

			foreach (var path in EnumerateAllFilesInSurfaceArea(enqueued, dequeued, fileDiscovered, mountProcessed))
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				if (DateTime.UtcNow >= nextStatusUpdateUTC)
				{
					rescanStatus.BackupAgentQueueSizes = _backupAgent.GetQueueSizes();

					statusUpdateCallback?.Invoke(rescanStatus);

					nextStatusUpdateUTC = DateTime.UtcNow.AddSeconds(0.4);
				}

				deletedPaths.Remove(path);

				rescanStatus.NumberOfFilesLeftToMatch = deletedPaths.Count;

				var fileState = _remoteFileStateCache.GetFileState(path);

				bool checkPath = false;

				if (fileState == null)
				{
					NonQuietDiagnosticOutput("[PR] - New path: {0}", path);
					checkPath = true;
				}
				else
				{
					var fileInfo = new FileInfo(fileState.Path);
					
					if ((fileState.FileSize == fileInfo.Length)
					 && (fileState.LastModifiedUTC == fileInfo.LastWriteTimeUtc))
						VerboseDiagnosticOutput("[PR] - Unchanged path: {0}", path);
					else
					{
						NonQuietDiagnosticOutput("[PR] - Changed path: {0}", path);
						checkPath = true;
					}
				}

				if (checkPath)
				{
					_backupAgent.CheckPath(path);

					if (_backupAgent.OpenFilesCount >= _parameters.QueueHighWaterMark)
					{
						while (_backupAgent.OpenFilesCount >= _parameters.QueueLowWaterMark)
							Thread.Sleep(TimeSpan.FromSeconds(10));
					}
				}
			}

			rescanStatus.BackupAgentQueueSizes = _backupAgent.GetQueueSizes();

			statusUpdateCallback?.Invoke(rescanStatus);

			if (deletedPaths.Count < 50)
				foreach (var deletedPath in deletedPaths)
					NonQuietDiagnosticOutput("[PR] - Deleted path: {0}", deletedPath);
			else
				NonQuietDiagnosticOutput("[PR] - Detected {0:##0,0} deleted paths", deletedPaths.Count);

			_backupAgent.CheckPaths(deletedPaths);

			NonQuietDiagnosticOutput("[PR] Periodic rescan complete");

			rescanStatus.IsRunning = false;

			statusUpdateCallback?.Invoke(rescanStatus);
		}
	}
}
