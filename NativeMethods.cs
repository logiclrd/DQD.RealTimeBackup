//using System;
//using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
//using System.Threading;

//using Microsoft.Win32.SafeHandles;

class NativeMethods
{
  public const int MAX_PATH = 4096;

  [StructLayout(LayoutKind.Sequential, Pack = 1)]
  public struct fanotify_event_metadata
  {
    public byte Version;
    public byte Reserved;
    public short MetadataLength;
    public int _Alignment;
    public long Mask;
    public int FileDescriptor;
    public int ProcessID;
  }

  public delegate void SignalHandler(int signal);

  [StructLayout(LayoutKind.Sequential)]
  public struct sigaction_t
  {
    public SignalHandler? sa_handler;
    public long sa_mask_0;
    public long sa_mask_1;
    public long sa_mask_2;
    public long sa_mask_3;
    public long sa_mask_4;
    public long sa_mask_5;
    public long sa_mask_6;
    public long sa_mask_7;
    public long sa_mask_8;
    public long sa_mask_9;
    public long sa_mask_10;
    public long sa_mask_11;
    public long sa_mask_12;
    public long sa_mask_13;
    public long sa_mask_14;
    public long sa_mask_15;
    public int sa_flags;
    public Action? sa_restorer;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct PollFD
  {
    public int FileDescriptor;
    public short RequestedEvents;
    public short ReturnedEvents;
  }

  public const int FileAccessNotifyClass_Notification = 0x00000000;

  public const int FileAccessNotifyReport_UniqueFileID = 0x00000200;

  public const int EventHeaderLength = 24;

  public const int FAN_ACCESS = 1;
  public const int FAN_MODIFY = 2;
  public const int FAN_CLOSE_WRITE = 8;
  public const int FAN_MOVED_FROM = 64;
  public const int FAN_MOVED_TO = 128;
  public const int FAN_MOVE = FAN_MOVED_FROM | FAN_MOVED_TO;
  public const int FAN_CREATE = 256;
  public const int FAN_DELETE = 512;

  public const int FAN_MARK_ADD = 1;
  public const int FAN_MARK_FILESYSTEM = 256;

  public const int FAN_EVENT_INFO_TYPE_FID = 1;

  public const int AT_FDCWD = -100;

  public const int O_RDONLY = 0;
  public const int O_NOFOLLOW = 131072;
  public const int O_NONBLOCK = 2048;
  public const int O_LARGEFILE = 0;
  public const int O_PATH = 0x00200000;

  public const int F_OK = 0;

  public const int POLLIN = 1;

  public const int INFTIM = -1;

  [DllImport("c")]
  public static extern int fanotify_init(int flags, int event_flags);
  [DllImport("c")]
  public static extern int fanotify_mark(IntPtr fanotify_fd, int flags, long mask, int dirfd, string pathname);
  [DllImport("c")]
  public static extern int posix_memalign(ref IntPtr memptr, IntPtr alignment, IntPtr size);
  [DllImport("c")]
  public static extern int access(string pathname, int mode);
  [DllImport("c")]
  public static extern int pipe(int[] pipeFDs);
  [DllImport("c")]
  public static extern int open(string pathname, int flags);
  [DllImport("c")]
  public static extern int open_by_handle_at(int dirfd, IntPtr handle, int flags);
  [DllImport("c")]
  public static extern int poll(PollFD[] fds, int nfds, int timeout);
  [DllImport("c")]
  public static extern int read(int fd, IntPtr buf, IntPtr count);
  [DllImport("c")]
  public static extern int write(int fd, byte[] buf, IntPtr count);
  [DllImport("c")]
  public static extern int close(int fd);
  [DllImport("c")]
  public static extern IntPtr readlink(string pathname, StringBuilder buf, int bufsiz);
  [DllImport("c")]
  public static extern int fstatfs(int fd, byte[] buf);

  [DllImport("c")]
  public static extern IntPtr setmntent(string filename, string type);
  [DllImport("c")]
  public static extern IntPtr getmntent(IntPtr fp);
  [DllImport("c")]
  public static extern int endmntent(IntPtr fp);

  /*
  [DllImport("libpthread.so.0")]
  public static extern int pthread_self();
  [DllImport("libpthread.so.0")]
  public static extern int pthread_kill(int thread, int sig);

  [DllImport("c")]
  public static extern int sigaction(int signum, ref sigaction_t act, IntPtr oldact);
  */


  public static void DecodeMountEntry(IntPtr mount, out string mnt_fsname, out string mnt_dir, out string? mnt_type, out string? mnt_opts, out int mnt_freq, out int mnt_passno)
  {
    unsafe
    {
      var stream = new UnmanagedMemoryStream((byte *)mount, 4 * IntPtr.Size + 8);

      var reader = new BinaryReader(stream);

      IntPtr mnt_fsname_addr;
      IntPtr mnt_dir_addr;
      IntPtr mnt_type_addr;
      IntPtr mnt_opts_addr;

      if (IntPtr.Size == 4)
      {
        mnt_fsname_addr = (IntPtr)reader.ReadInt32();
        mnt_dir_addr = (IntPtr)reader.ReadInt32();
        mnt_type_addr = (IntPtr)reader.ReadInt32();
        mnt_opts_addr = (IntPtr)reader.ReadInt32();
      }
      else if (IntPtr.Size == 8)
      {
        mnt_fsname_addr = (IntPtr)reader.ReadInt64();
        mnt_dir_addr = (IntPtr)reader.ReadInt64();
        mnt_type_addr = (IntPtr)reader.ReadInt64();
        mnt_opts_addr = (IntPtr)reader.ReadInt64();
      }
      else
        throw new Exception("Sanity failure"); 

      mnt_freq = reader.ReadInt32();
      mnt_passno = reader.ReadInt32();

      mnt_fsname = (mnt_fsname_addr == IntPtr.Zero) ? "" : Marshal.PtrToStringAuto(mnt_fsname_addr)!;
      mnt_dir = (mnt_dir_addr == IntPtr.Zero) ? "" : Marshal.PtrToStringAuto(mnt_dir_addr)!;
      mnt_type = (mnt_type_addr == IntPtr.Zero) ? null : Marshal.PtrToStringAuto(mnt_type_addr);
      mnt_opts = (mnt_opts_addr == IntPtr.Zero) ? null : Marshal.PtrToStringAuto(mnt_opts_addr);
    }
  }
}
