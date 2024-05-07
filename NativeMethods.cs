using System;
using System.Runtime.InteropServices;
using System.Text;

class NativeMethods
{
  public const int MAX_PATH = 4096;

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
  public const int FAN_MOVED_FROM = 64;
  public const int FAN_MOVED_TO = 128;
  public const int FAN_MOVE = FAN_MOVED_FROM | FAN_MOVED_TO;
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
