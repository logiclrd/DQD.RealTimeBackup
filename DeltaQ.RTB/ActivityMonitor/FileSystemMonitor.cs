using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using DeltaQ.RTB.Interop;
using Microsoft.VisualBasic;

namespace DeltaQ.RTB.ActivityMonitor
{
	public class FileSystemMonitor : IFileSystemMonitor
	{
		OperatingParameters _parameters;

		IMountTable _mountTable;
		Func<IFileAccessNotify> _fileAccessNotifyFactory;
		IOpenByHandleAt _openByHandleAt;

		IFileAccessNotify? _fileAccessNotify;

		public FileSystemMonitor(OperatingParameters parameters, IMountTable mountTable, Func<IFileAccessNotify> fileAccessNotifyFactory, IOpenByHandleAt openByHandleAt)
		{
			_parameters = parameters;

			_mountTable = mountTable;
			_fileAccessNotifyFactory = fileAccessNotifyFactory;
			_openByHandleAt = openByHandleAt;

			_shutdownSource = new CancellationTokenSource();
		}

		public event EventHandler<PathUpdate>? PathUpdate;
		public event EventHandler<PathMove>? PathMove;
		public event EventHandler<PathDelete>? PathDelete;

		volatile int _threadCount = 0;
		object _threadCountSync = new object();

		StringBuilder _pathNameBuffer = new StringBuilder(NativeMethods.MAX_PATH);

		internal void ProcessEvent(FileAccessNotifyEvent @event)
		{
			if (!@event.InformationStructures.Any())
			{
				Console.Error.WriteLine("  *** No fanotify info structures received with event with mask " + @event.Metadata.Mask);
				return;
			}

			string? ResolvePath(FileAccessNotifyEventInfoType type)
			{
				foreach (var info in @event.InformationStructures)
				{
					if (info.Type == type)
					{
						if (!string.IsNullOrEmpty(info.FileName))
						{
							string path = info.FileName!;

							if (info.FileHandle != null)
							{
								if (!MountDescriptorByFileSystemID.TryGetValue(info.FileSystemID, out var mountDescriptor))
								{
									Console.Error.WriteLine("Using file system mount fallback");
									mountDescriptor = NativeMethods.AT_FDCWD;
								}

								using (var openHandle = _openByHandleAt.Open(mountDescriptor, info.FileHandle!))
								{
									if (openHandle != null)
									{
										string containerPath = openHandle.ReadLink();

										path = Path.Combine(containerPath, path);

										return path;
									}
								}
							}
						}
					}
				}

				return null;
			}

			string? path = ResolvePath(FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName);
			string? pathFrom = ResolvePath(FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_From);
			string? pathTo = ResolvePath(FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_To);

			if (path != null)
			{
				if (@event.Metadata.Mask.HasFlag(FileAccessNotifyEventMask.ChildDeleted))
					PathDelete?.Invoke(this, new PathDelete(path));
				if (@event.Metadata.Mask.HasFlag(FileAccessNotifyEventMask.Modified))
					PathUpdate?.Invoke(this, new PathUpdate(path));
			}

			if ((pathFrom != null) && (pathTo != null))
			{
				if (@event.Metadata.Mask.HasFlag(FileAccessNotifyEventMask.ChildMoved))
					PathMove?.Invoke(this, new PathMove(pathFrom, pathTo));
			}
		}

		internal Dictionary<long, int> MountDescriptorByFileSystemID = new Dictionary<long, int>();

		List<IMount>? _surfaceArea;

		// TODO: make surface area a first-class entity so that it can be used even when not monitoring the filesystem

		internal void SetUpFANotify()
		{
			if (_fileAccessNotify == null)
				throw new Exception("Sanity check failure: Cannot set up FANotify before opening FANotify handles");

			_fileAccessNotify.MarkPath("/");

			var openMount = _mountTable.OpenMountForFileSystem("/");

			MountDescriptorByFileSystemID[openMount.FileSystemID] = openMount.FileDescriptor;

			var mountsToMark = _mountTable.EnumerateMounts().ToList();

			var mountsByDevice = mountsToMark
				.GroupBy(key => key.DeviceName)
				.Select(grouping => grouping.ToList())
				.ToList();

			foreach (var mountSet in mountsByDevice)
			{
				for (int i = mountSet.Count - 1; i >= 0; i--)
				{
					if (!string.IsNullOrWhiteSpace(mountSet[i].Root) && (mountSet[i].Root != "/"))
					{
						Console.WriteLine("Discarding mount because it isn't at root: {0}[{1}] @ {2}", mountSet[i].DeviceName, mountSet[i].Root, mountSet[i].MountPoint);
						mountSet.RemoveAt(i);
						continue;
					}

					if (!_parameters.MonitorFileSystemTypes.Contains(mountSet[i].FileSystemType))
					{
						Console.WriteLine("Discarding mount because of the filesystem type: {0}[{1}] @ {2}", mountSet[i].DeviceName, mountSet[i].Root, mountSet[i].MountPoint);
						mountSet.RemoveAt(i);
						continue;
					}
				}

				if (mountSet.Count > 1)
				{
					int preferredMountPointIndex = -1;

					for (int i = mountSet.Count - 1; i >= 0; i--)
					{
						if (_parameters.PreferredMountPoints.Contains(mountSet[i].MountPoint))
						{
							if (preferredMountPointIndex >= 0)
								throw new Exception($"More than one mount point refers to device {mountSet[0].DeviceName}. One of these needs to be explicitly selected using the PreferredMountPoint option. Presently, more than one is selected.");

							preferredMountPointIndex = i;
						}
					}

					if (preferredMountPointIndex < 0)
						throw new Exception($"More than one mount point refers to device {mountSet[0].DeviceName}. One of these needs to be explicitly selected using the PreferredMountPoint option. Presently, none is selected.");

					var preferredMount = mountSet[preferredMountPointIndex];

					mountSet.Clear();
					mountSet.Add(preferredMount);
				}
			}

			_surfaceArea = mountsByDevice.SelectMany(m => m).ToList();

			Console.WriteLine("About to mark");

			foreach (var mount in _surfaceArea)
			{
				if (mount.FileSystemType != "zfs")
				{
					if (!mount.TestDeviceAccess() || !mount.DeviceName.StartsWith("/"))
					{
						Console.WriteLine("Discarding mount because its device is inaccessible: {0}[{1}] @ {2}", mount.DeviceName, mount.Root, mount.MountPoint);
						continue;
					}
				}

				if ((mount.MountPoint != null) && (mount.MountPoint != "/"))
				{
					Console.WriteLine("Marking: {0}", mount.MountPoint);

					_fileAccessNotify.MarkPath(mount.MountPoint);

					openMount = _mountTable.OpenMountForFileSystem(mount.MountPoint);

					MountDescriptorByFileSystemID[openMount.FileSystemID] = openMount.FileDescriptor;
				}
			}

			Console.WriteLine("Done marking");
		}

		internal void InitializeFileAccessNotify()
		{
			_fileAccessNotify = _fileAccessNotifyFactory();
		}

		internal void MonitorFileActivityThreadProc()
		{
			Interlocked.Increment(ref _threadCount);

			try
			{
				_fileAccessNotify?.MonitorEvents(
					ProcessEvent,
					_shutdownSource.Token);
			}
			finally
			{
				Interlocked.Decrement(ref _threadCount);

				lock (_threadCountSync)
					Monitor.PulseAll(_threadCountSync);
			}
		}

		bool _started;
		CancellationTokenSource _shutdownSource;

		public void Start()
		{
			if (_started)
				return;

			InitializeFileAccessNotify();

			if (_fileAccessNotify == null)
				throw new Exception("Internal error");

			SetUpFANotify();

			_started = true;

			Task.Run(() => MonitorFileActivityThreadProc());
		}

		public void Stop()
		{
			_shutdownSource.Cancel();
			_fileAccessNotify?.Dispose();

			lock (_threadCountSync)
			{
				while (_threadCount > 0)
					Monitor.Wait(_threadCountSync);
			}

			_shutdownSource = new CancellationTokenSource();
			_started = false;
		}
	}
}

