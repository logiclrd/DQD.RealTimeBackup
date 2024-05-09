using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class FileSystemMonitor : IFileSystemMonitor
{
  IMountTable _mountTable;

  public FileSystemMonitor(IMountTable mountTable)
  {
    _mountTable = mountTable;

    _shutdownSource = new CancellationTokenSource();
  }

  public event EventHandler<PathUpdate>? PathUpdate;
  public event EventHandler<PathMove>? PathMove;

  volatile int _threadCount = 0;
  object _threadCountSync = new object();

  FileAccessNotify? _fileAccessNotify;

  StringBuilder _pathNameBuffer = new StringBuilder(NativeMethods.MAX_PATH);

  void ProcessEvent(FileAccessNotifyEvent @event)
  {
    int mask = unchecked((int)@event.Metadata.Mask);

    if (@event.AdditionalDataLength < 16)
      throw new Exception("Insufficient bytes for fanotify_event_info_header, fanotify_event_info_fid and struct file_handle");

    // fanotify_event_info_header

    byte infoType = Marshal.ReadByte(@event.AdditionalData, 0);
    byte padding = Marshal.ReadByte(@event.AdditionalData, 1);
    int length = Marshal.ReadInt16(@event.AdditionalData, 2);

    if (infoType != NativeMethods.FAN_EVENT_INFO_TYPE_FID)
      throw new Exception("Received unexpected event info type: " + infoType);

    // fanotify_event_info_fid
    long fsid = Marshal.ReadInt64(@event.AdditionalData, 4);

    // fanotify_event_info_fid -> file_handle
    IntPtr fileHandle = @event.AdditionalData + 12;

    int handleBytes = Marshal.ReadInt32(fileHandle, 0);

    if (@event.AdditionalDataLength < 12 + handleBytes)
      throw new Exception("Insufficient bytes for struct file_handle with length " + handleBytes);

    if (!MountDescriptorByFileSystemID.TryGetValue(fsid, out var mountDescriptor))
    {
      Console.WriteLine("Using file system mount fallback");
      mountDescriptor = NativeMethods.AT_FDCWD;
    }

    int fd = NativeMethods.open_by_handle_at(
      mountDescriptor,
      fileHandle,
      NativeMethods.O_RDONLY | NativeMethods.O_NONBLOCK | NativeMethods.O_LARGEFILE | NativeMethods.O_PATH);

    if (fd > 0)
    {
      string path;

      try
      {
        string linkPath = "/proc/self/fd/" + fd;

        _pathNameBuffer.Length = NativeMethods.MAX_PATH;

        var len = NativeMethods.readlink(linkPath, _pathNameBuffer, _pathNameBuffer.Length);

        if (len < 0)
          return;

        path = _pathNameBuffer.ToString(0, (int)len);
      }
      finally
      {
        NativeMethods.close(fd);
      }

      if ((mask & NativeMethods.FAN_MOVE) != 0)
        PathMove?.Invoke(this, new PathMove(path, (mask & NativeMethods.FAN_MOVED_FROM) != 0 ? MoveType.From : MoveType.To));
      else
        PathUpdate?.Invoke(this, new PathUpdate(path, (mask & NativeMethods.FAN_DELETE) != 0 ? UpdateType.ChildRemoved : UpdateType.ContentUpdated));
    }
  }

  Dictionary<long, int> MountDescriptorByFileSystemID = new Dictionary<long, int>();

  void SetUpFANotify()
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

  void MonitorFileActivity()
  {
    Interlocked.Increment(ref _threadCount);

    try
    {
      _fileAccessNotify = new FileAccessNotify();

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
