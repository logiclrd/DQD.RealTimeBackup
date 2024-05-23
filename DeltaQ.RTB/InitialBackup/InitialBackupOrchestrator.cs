using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.FileSystem;
using DeltaQ.RTB.SurfaceArea;

namespace DeltaQ.RTB.InitialBackup
{
	public class InitialBackupOrchestrator : IInitialBackupOrchestrator
	{
		ISurfaceArea _surfaceArea;
		IBackupAgent _backupAgent;
		IZFS _zfs;

		public InitialBackupOrchestrator(ISurfaceArea surfaceArea, IBackupAgent backupAgent, IZFS zfs)
		{
			_surfaceArea = surfaceArea;
			_backupAgent = backupAgent;
			_zfs = zfs;
		}

		public void PerformInitialBackup(Action<InitialBackupStatus> statusUpdateCallback)
		{
			InitialBackupStatus status = new InitialBackupStatus();

			// Start queue thread
			Thread thread = new Thread(
				arg =>
				{
					var self = (Thread)arg!;

					var intermediate = new List<string>();

					self.Priority = ThreadPriority.Lowest;

					foreach (var path in EnumerateAllFilesInSurfaceArea(status))
					{
						intermediate.Add(path);

						if (intermediate.Count == 100)
						{
							self.Priority = ThreadPriority.AboveNormal;

							_backupAgent.CheckPaths(intermediate);
							intermediate.Clear();

							self.Priority = ThreadPriority.Lowest;
						}
					}

					self.Priority = ThreadPriority.Normal;
				});

			thread.Start(thread);

			// Poll status
			while (true)
			{
				status.BackupAgentQueueSizes = _backupAgent.GetQueueSizes();
				status.ZFSSnapshotCount = _zfs.CurrentSnapshotCount;

				statusUpdateCallback?.Invoke(status);

				if (!thread.IsAlive && !status.BackupAgentQueueSizes.IsBackupAgentBusy)
					break;

				Thread.Sleep(TimeSpan.FromSeconds(0.5));
			}
		}

		IEnumerable<string> EnumerateAllFilesInSurfaceArea(InitialBackupStatus status)
		{
			foreach (var mount in _surfaceArea.Mounts)
			{
				Queue<string> directoryQueue = new Queue<string>();

				directoryQueue.Enqueue(mount.MountPoint);

				while (directoryQueue.Any())
				{
					string path = directoryQueue.Dequeue();

					Interlocked.Decrement(ref status.DirectoryQueueSize);

					var enumerator = new FileSystemEnumerable<string>(
						path,
						(ref FileSystemEntry entry) => entry.ToFullPath());

					enumerator.ShouldIncludePredicate =
						(ref FileSystemEntry entry) =>
						{
							if (!entry.IsDirectory)
								return true;
							else
							{
								directoryQueue.Enqueue(entry.ToFullPath());
								Interlocked.Increment(ref status.DirectoryQueueSize);
								return false;
							}
						};

					foreach (string filePath in enumerator)
					{
						Interlocked.Increment(ref status.FilesDiscovered);
						yield return filePath;
					}
				}

				Interlocked.Increment(ref status.MountsProcessed);
			}
		}
	}
}
