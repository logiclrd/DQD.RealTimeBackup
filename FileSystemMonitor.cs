using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Microsoft.Win32.SafeHandles;

public class FileSystemMonitor
{
  public FileSystemMonitor()
  {
    _shutdownSource = new CancellationTokenSource();
  }

  volatile int _threadCount = 0;
  object _threadCountSync = new object();

  public event EventHandler<PathUpdate>? PathUpdate;
  public event EventHandler<PathMove>? PathMove;

  StringBuilder _pathNameBuffer = new StringBuilder(NativeMethods.MAX_PATH);

  void ProcessEvent(NativeMethods.fanotify_event_metadata metadata, IntPtr nextBytes, int nextBytesCount)
  {
    int mask = unchecked((int)metadata.Mask);

    if (nextBytesCount < 16)
      throw new Exception("Insufficient bytes for fanotify_event_info_header, fanotify_event_info_fid and struct file_handle");

    // fanotify_event_info_header

    byte infoType = Marshal.ReadByte(nextBytes, 0);
    byte padding = Marshal.ReadByte(nextBytes, 1);
    int length = Marshal.ReadInt16(nextBytes, 2);

    if (infoType != NativeMethods.FAN_EVENT_INFO_TYPE_FID)
      throw new Exception("Received unexpected event info type: " + infoType);

    // fanotify_event_info_fid
    long fsid = Marshal.ReadInt64(nextBytes, 4);

    // fanotify_event_info_fid -> file_handle
    IntPtr fileHandle = nextBytes + 12;

    int handleBytes = Marshal.ReadInt32(fileHandle, 0);

    if (nextBytesCount < 12 + handleBytes)
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

  void MarkPath(IntPtr fan_fd, string path, bool fatal)
  {

    int res = NativeMethods.fanotify_mark(
      fan_fd,
      NativeMethods.FAN_MARK_ADD | NativeMethods.FAN_MARK_FILESYSTEM,
      NativeMethods.FAN_ACCESS | NativeMethods.FAN_MODIFY | NativeMethods.FAN_DELETE | NativeMethods.FAN_MOVE,
      NativeMethods.AT_FDCWD,
      path);

    if (res < 0)
    {
      if (fatal)
        throw new Exception("Failed to add watch for " + path);
      else
        Console.Error.WriteLine("Failed to add watch for " + path);
    }
  }

  Dictionary<long, int> MountDescriptorByFileSystemID = new Dictionary<long, int>();

  void OpenMountForFileSystem(string mountPointPath)
  {
    int fd = NativeMethods.open(mountPointPath, NativeMethods.O_RDONLY | NativeMethods.O_NOFOLLOW);
    byte[] s = new byte[128];

    if (fd < 0)
    {
      Console.Error.WriteLine("Failed to open mount point: {0}", mountPointPath);
      return;
    }

    if (NativeMethods.fstatfs(fd, s) < 0)
    {
      Console.Error.WriteLine("Failed to stat mount point: {0}", mountPointPath);
      return;
    }

    long fsid = BitConverter.ToInt64(s, 56);

    MountDescriptorByFileSystemID[fsid] = fd;
  }

  void SetUpFANotify(int fan_fd)
  {
    MarkPath(fan_fd, "/", false);
    OpenMountForFileSystem("/");

    IntPtr mounts = NativeMethods.setmntent("/proc/self/mounts", "r");
    if (mounts == IntPtr.Zero)
      throw new Exception("setmntent failed");

    IntPtr mount = NativeMethods.getmntent(mounts);

    while (mount != IntPtr.Zero)
    {
      NativeMethods.DecodeMountEntry(mount, out string? mnt_fsname, out string? mnt_dir, out string? mnt_type, out string? mnt_opts, out int mnt_freq, out int mnt_passno);

      mount = NativeMethods.getmntent(mounts);

      if ((mnt_fsname == null) || ((NativeMethods.access(mnt_fsname, NativeMethods.F_OK) != 0) || !mnt_fsname.StartsWith("/")))
      {
        if (mnt_type != "zfs")
          continue;
      }

      if ((mnt_dir != null) && (mnt_dir != "/"))
      {
        MarkPath(fan_fd, mnt_dir, false);
        OpenMountForFileSystem(mnt_dir);
      }
    }

    NativeMethods.endmntent(mounts);
  }

  const int BufferSize = 256 * 1024;

  int _fanFD = -1;

  void MonitorFileActivity()
  {
    Interlocked.Increment(ref _threadCount);

    try
    {
      _fanFD = NativeMethods.fanotify_init(NativeMethods.FileAccessNotifyClass_Notification | NativeMethods.FileAccessNotifyReport_UniqueFileID, 0);

      if (_fanFD < 0)
        throw new Exception("Cannot initialize fanotify");

      SetUpFANotify(_fanFD);

      IntPtr buffer = IntPtr.Zero;

      int result = NativeMethods.posix_memalign(ref buffer, 4096, BufferSize);

      if ((result != 0) || (buffer == IntPtr.Zero))
        throw new Exception("Failed to allocate buffer");

      while (!_shutdownSource.Token.IsCancellationRequested)
      {
        int readSize = NativeMethods.read(_fanFD, buffer, BufferSize);

        if (readSize < 0)
          throw new Exception("Read error");

        unsafe
        {
          IntPtr ptr = buffer;
          IntPtr endPtr = ptr + readSize;

          while (true)
          {
            int eventLength = Marshal.ReadInt32(ptr);

            if ((eventLength < NativeMethods.EventHeaderLength) || (ptr + eventLength > endPtr))
              break;

            var additionalDataPtr = ptr + NativeMethods.EventHeaderLength;

            ptr += eventLength;

            var eventStream = new UnmanagedMemoryStream((byte *)ptr, eventLength);

            var eventReader = new BinaryReader(eventStream);

            NativeMethods.fanotify_event_metadata metadata = new();

            metadata.Version = eventReader.ReadByte();
            metadata.Reserved = eventReader.ReadByte();
            metadata.MetadataLength = eventReader.ReadInt16();
            metadata._Alignment = eventReader.ReadInt32();
            metadata.Mask = eventReader.ReadInt64();
            metadata.FileDescriptor = eventReader.ReadInt32();
            metadata.ProcessID = eventReader.ReadInt32();

            ProcessEvent(metadata, additionalDataPtr, eventLength - NativeMethods.EventHeaderLength);
          }
        }
      }
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
    NativeMethods.close(_fanFD);

    lock (_threadCountSync)
    {
      while (_threadCount > 0)
        Monitor.Wait(_threadCountSync);
    }

    _shutdownSource = new CancellationTokenSource();
    _started = false;
  }
}
