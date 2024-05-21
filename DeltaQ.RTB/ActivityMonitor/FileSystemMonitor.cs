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

namespace DeltaQ.RTB.ActivityMonitor
{
	public class FileSystemMonitor : IFileSystemMonitor
	{
		IMountTable _mountTable;
		Func<IFileAccessNotify> _fileAccessNotifyFactory;
		IOpenByHandleAt _openByHandleAt;

		IFileAccessNotify? _fileAccessNotify;

		public FileSystemMonitor(IMountTable mountTable, Func<IFileAccessNotify> fileAccessNotifyFactory, IOpenByHandleAt openByHandleAt)
		{
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

		internal void SetUpFANotify()
		{
			if (_fileAccessNotify == null)
				throw new Exception("Sanity check failure: Cannot set up FANotify before opening FANotify handles");

			_fileAccessNotify.MarkPath("/");

			var openMount = _mountTable.OpenMountForFileSystem("/");

			MountDescriptorByFileSystemID[openMount.FileSystemID] = openMount.FileDescriptor;

			foreach (var mount in _mountTable.EnumerateMounts())
			{
				if (mount.Type != "zfs")
				{
					if (!mount.TestDeviceAccess() || !mount.DeviceName.StartsWith("/"))
						continue;
				}

				if ((mount.MountPoint != null) && (mount.MountPoint != "/"))
				{
					_fileAccessNotify.MarkPath(mount.MountPoint);

					openMount = _mountTable.OpenMountForFileSystem(mount.MountPoint);

					MountDescriptorByFileSystemID[openMount.FileSystemID] = openMount.FileDescriptor;
				}
			}
		}

		internal void InitializeFileAccessNotify()
		{
			_fileAccessNotify = _fileAccessNotifyFactory();
		}

		internal void MonitorFileActivity()
		{
			Interlocked.Increment(ref _threadCount);

			try
			{
				InitializeFileAccessNotify();

				if (_fileAccessNotify == null)
					throw new Exception("Internal error");

				SetUpFANotify();

				_fileAccessNotify.MonitorEvents(
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

			_started = true;

			Task.Run(() => MonitorFileActivity());
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

