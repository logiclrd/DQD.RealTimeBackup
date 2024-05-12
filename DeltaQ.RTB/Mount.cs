namespace DeltaQ.RTB
{
  public class Mount : IMount
  {
    string _deviceName;
    string _mountPoint;
    string? _type;
    string? _options;
    int _frequency;
    int _passNumber;

    public string DeviceName => _deviceName;
    public string MountPoint => _mountPoint;
    public string? Type => _type;
    public string? Options => _options;
    public int Frequency => _frequency;
    public int PassNumber => _passNumber;

    public Mount(string mnt_fsname, string mnt_dir, string? mnt_type, string? mnt_opts, int mnt_freq, int mnt_passno)
    {
      _deviceName = mnt_fsname;
      _mountPoint = mnt_dir;
      _type = mnt_type;
      _options = mnt_opts;
      _frequency = mnt_freq;
      _passNumber = mnt_passno;
    }

    public bool TestDeviceAccess()
    {
      if (_deviceName != null)
        return (NativeMethods.access(_deviceName, NativeMethods.F_OK) == 0);
      else
        return false;
    }
  }
}
