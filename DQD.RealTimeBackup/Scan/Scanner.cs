using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;

using DQD.RealTimeBackup.Interop;
using DQD.RealTimeBackup.SurfaceArea;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.Scan
{
	public abstract class Scanner : DiagnosticOutputBase
	{
		ISurfaceArea _surfaceArea;
		IStat _stat;

		public Scanner(ISurfaceArea surfaceArea, IStat stat)
		{
			_surfaceArea = surfaceArea;
			_stat = stat;
		}

		protected IEnumerable<string> EnumerateAllFilesInSurfaceArea(Action? enqueued = null, Action? dequeued = null, Action? fileDiscovered = null, Action? mountProcessed = null)
		{
			foreach (var mount in _surfaceArea.Mounts)
			{
				var mountPointStat = _stat.Stat(mount.MountPoint);

				Queue<string> directoryQueue = new Queue<string>();

				directoryQueue.Enqueue(mount.MountPoint);
				enqueued?.Invoke();

				bool isRootOfMount = true;

				while (directoryQueue.Any())
				{
					string path = directoryQueue.Dequeue();
					dequeued?.Invoke();

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
							if (isRootOfMount && (entry.FileName == ".zfs"))
								return false;

							if (!entry.IsDirectory)
								return true;
							else
							{
								// Make sure we don't follow symlinks or cross devices.
								string path = entry.ToFullPath();

								var subdirStat = _stat.Stat(path);

								if ((subdirStat.ContainerDeviceID == mountPointStat.ContainerDeviceID)
								 && (File.ResolveLinkTarget(path, returnFinalTarget: false) == null))
								{
									directoryQueue.Enqueue(entry.ToFullPath());
									enqueued?.Invoke();
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

							fileDiscovered?.Invoke();
							yield return enumerator.Current;
						}
					}

					isRootOfMount = false;
				}

				mountProcessed?.Invoke();
			}
		}
	}
}
