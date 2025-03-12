using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.Interop;
using DQD.RealTimeBackup.SurfaceArea;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.ActivityMonitor
{
	public class FileSystemMonitor : DiagnosticOutputBase, IFileSystemMonitor
	{
		OperatingParameters _parameters;

		IErrorLogger _errorLogger;
		ISurfaceArea _surfaceArea;
		IMountTable _mountTable;
		Func<IFileAccessNotify> _fileAccessNotifyFactory;
		IOpenByHandleAt _openByHandleAt;

		IFileAccessNotify? _fileAccessNotify;

		public FileSystemMonitor(OperatingParameters parameters, IErrorLogger errorLogger, ISurfaceArea surfaceArea, IMountTable mountTable, Func<IFileAccessNotify> fileAccessNotifyFactory, IOpenByHandleAt openByHandleAt)
		{
			_parameters = parameters;

			_errorLogger = errorLogger;
			_surfaceArea = surfaceArea;
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

		internal void ProcessEvent(FileAccessNotifyEvent @event)
		{
			try
			{
				RunningState.Instance.FileSystemMonitor.State = FileSystemMonitorState.ProcessEvent;
				RunningState.Instance.FileSystemMonitor.Event = @event;

				bool lostEvents = @event.Metadata.Mask.HasFlag(FileAccessNotifyEventMask.LostEvents);

				if (lostEvents)
				{
					Console.Error.WriteLine("  *** Received notification of lost fanotify events");
					_errorLogger.LogError("Some filesystem notification events were lost. The backup of one or more files may be out-of-date until the next rescan.", ErrorLogger.Summary.SystemError);
				}

				if (!@event.InformationStructures.Any())
				{
					if (!lostEvents)
					{
						Console.Error.WriteLine("  *** No fanotify info structures received with event with mask " + @event.Metadata.Mask);
						_errorLogger.LogError("fanotify event with mask " + @event.Metadata.Mask + " was received with no info structures", ErrorLogger.Summary.InternalError);
					}

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
										_errorLogger.LogError("fanotify event was received with a FileSystemID that could not be resolved to a mount fd", ErrorLogger.Summary.InternalError);
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
			finally
			{
				RunningState.Instance.FileSystemMonitor.State = FileSystemMonitorState.Idle;
			}
		}

		internal Dictionary<long, int> MountDescriptorByFileSystemID = new Dictionary<long, int>();

		internal void SetUpFANotify()
		{
			if (_fileAccessNotify == null)
				throw new Exception("Sanity check failure: Cannot set up FANotify before opening FANotify handles");

			OnDiagnosticOutput("About to mark");

			foreach (var mount in _surfaceArea.Mounts)
			{
				OnDiagnosticOutput("Marking: {0}", mount.MountPoint);

				_fileAccessNotify.MarkPath(mount.MountPoint);

				var openMount = _mountTable.OpenMountForFileSystem(mount.MountPoint);

				MountDescriptorByFileSystemID[openMount.FileSystemID] = openMount.FileDescriptor;
			}

			OnDiagnosticOutput("Done marking");
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

