using System;
using System.Collections.Generic;
using System.Threading;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.FileSystem;
using DeltaQ.RTB.Interop;
using DeltaQ.RTB.SurfaceArea;

namespace DeltaQ.RTB.Scan
{
	public class InitialBackupOrchestrator : Scanner, IInitialBackupOrchestrator
	{
		OperatingParameters _parameters;

		IBackupAgent _backupAgent;
		IZFS _zfs;
		IStat _stat;

		public InitialBackupOrchestrator(OperatingParameters parameters, ISurfaceArea surfaceArea, IBackupAgent backupAgent, IZFS zfs, IStat stat)
			: base(surfaceArea, stat)
		{
			_parameters = parameters;

			_backupAgent = backupAgent;
			_zfs = zfs;
			_stat = stat;
		}

		public void PerformInitialBackup(Action<InitialBackupStatus> statusUpdateCallback, CancellationToken cancellationToken)
		{
			InitialBackupStatus status = new InitialBackupStatus();

			// Start queue thread
			Thread thread = new Thread(
				arg =>
				{
					var self = (Thread)arg!;

					var intermediate = new List<string>();

					self.Priority = ThreadPriority.Lowest;

					Action enqueued =
						() => Interlocked.Increment(ref status.DirectoryQueueSize);
					Action dequeued =
						() => Interlocked.Decrement(ref status.DirectoryQueueSize);
					Action fileDiscovered =
						() => Interlocked.Increment(ref status.FilesDiscovered);
					Action mountProcessed =
						() => Interlocked.Increment(ref status.MountsProcessed);

					foreach (var path in EnumerateAllFilesInSurfaceArea(enqueued, dequeued, fileDiscovered, mountProcessed))
					{
						if (cancellationToken.IsCancellationRequested)
							break;

						intermediate.Add(path);

						if (intermediate.Count == 100)
						{
							self.Priority = ThreadPriority.AboveNormal;

							_backupAgent.CheckPaths(intermediate);
							intermediate.Clear();

							if (_backupAgent.OpenFilesCount >= _parameters.QueueHighWaterMark)
							{
								while (_backupAgent.OpenFilesCount >= _parameters.QueueLowWaterMark)
									Thread.Sleep(TimeSpan.FromSeconds(10));
							}

							self.Priority = ThreadPriority.Lowest;
						}
					}

					self.Priority = ThreadPriority.Normal;
				});

			thread.Name = "Initial Backup Queue Thread";
			thread.Start(thread);

			// Poll status
			while (!cancellationToken.IsCancellationRequested)
			{
				status.BackupAgentQueueSizes = _backupAgent.GetQueueSizes();
				status.ZFSSnapshotCount = _zfs.CurrentSnapshotCount;

				statusUpdateCallback?.Invoke(status);

				if (!thread.IsAlive && !status.BackupAgentQueueSizes.IsBackupAgentBusy)
					break;

				Thread.Sleep(TimeSpan.FromSeconds(0.5));
			}
		}
	}
}
