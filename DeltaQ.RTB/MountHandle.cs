using System;

namespace DeltaQ.RTB
{
  public class MountHandle : IMountHandle
  {
    int _fd;
    string _mountPointPath;

    public MountHandle(int fd, string mountPointPath)
    {
      _fd = fd;
      _mountPointPath = mountPointPath;
    }

    public int FileDescriptor => _fd;

    public long FileSystemID
    {
      get
      {
        byte[] s = new byte[128];

        if (NativeMethods.fstatfs(_fd, s) < 0)
          throw new Exception($"Failed to stat mount point: {_mountPointPath}");

        return BitConverter.ToInt64(s, 56);
      }
    }
  }
}
