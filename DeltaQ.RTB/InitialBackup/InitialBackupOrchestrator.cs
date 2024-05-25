using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.FileSystem;
using DeltaQ.RTB.Interop;
using DeltaQ.RTB.SurfaceArea;

namespace DeltaQ.RTB.InitialBackup
{
	public class InitialBackupOrchestrator : IInitialBackupOrchestrator
	{
		OperatingParameters _parameters;

		ISurfaceArea _surfaceArea;
		IBackupAgent _backupAgent;
		IZFS _zfs;
		IStat _stat;

		public InitialBackupOrchestrator(OperatingParameters parameters, ISurfaceArea surfaceArea, IBackupAgent backupAgent, IZFS zfs, IStat stat)
		{
			_parameters = parameters;

			_surfaceArea = surfaceArea;
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

					foreach (var path in EnumerateAllFilesInSurfaceArea(status))
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

		IEnumerable<string> EnumerateAllFilesInSurfaceArea(InitialBackupStatus status)
		{
			foreach (var mount in _surfaceArea.Mounts)
			{
				var mountPointStat = _stat.Stat(mount.MountPoint);

				Queue<string> directoryQueue = new Queue<string>();

				directoryQueue.Enqueue(mount.MountPoint);

				while (directoryQueue.Any())
				{
					string path = directoryQueue.Dequeue();

					Interlocked.Decrement(ref status.DirectoryQueueSize);

					FileSystemEnumerable<string> enumerable;

					try
					{
						enumerable = new FileSystemEnumerable<string>(
							path,
							(ref FileSystemEntry entry) => entry.ToFullPath());
					}
					catch (DirectoryNotFoundException)
					{
						// Removed between enumerating the parent and getting here.
						continue;
					}

					enumerable.ShouldIncludePredicate =
						(ref FileSystemEntry entry) =>
						{
							if (!entry.IsDirectory)
								return true;
							else
							{
								// Make sure we don't cross devices.
								string path = entry.ToFullPath();

								var subdirStat = _stat.Stat(path);

								if (subdirStat.ContainerDeviceID == mountPointStat.ContainerDeviceID)
								{
									directoryQueue.Enqueue(entry.ToFullPath());
									Interlocked.Increment(ref status.DirectoryQueueSize);
								}

								return false;
							}
						};

					// Things come and go quickly. This applies everywhere in the filesystem, but especially in /tmp, out of which we may be
					// enumerating files. It is entirely possible that a directory may get removed between its discovery and its enumeration.
					// Unfortunately, we can't have a yield return statement inside a try/catch, so we have to expand out the foreach and
					// don't get the nice syntax.

					using (var enumerator = enumerable.GetEnumerator())
					{
						while (true)
						{
							bool hasNext = false;

							try
							{
								hasNext = enumerator.MoveNext();
							}
							catch (DirectoryNotFoundException) { }

							if (!hasNext)
								break;

							Interlocked.Increment(ref status.FilesDiscovered);
							yield return enumerator.Current;
						}
					}
				}

				Interlocked.Increment(ref status.MountsProcessed);
			}
		}
	}
}
