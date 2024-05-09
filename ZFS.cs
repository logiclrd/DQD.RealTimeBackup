using System;
using System.Diagnostics;

public class ZFS : IZFS
{
  const string ZFSBinaryPath = "/usr/sbin/zfs";

  protected string _deviceName;
  protected string _mountPoint;

  public string DeviceName => _deviceName;
  public string MountPoint => _mountPoint;

  public ZFS(string deviceName)
    : this(FindVolume(deviceName))
  {
  }

  public ZFS(ZFSVolume volume)
    : this(
        volume.DeviceName ?? throw new ArgumentNullException("volume.DeviceName"),
        volume.MountPoint ?? throw new ArgumentNullException("volume.MountPoint"))
  {
  }

  ZFS(string deviceName, string mountPoint)
  {
    _deviceName = deviceName;
    _mountPoint = mountPoint;
  }

  protected static void ExecuteZFSCommand(string command)
  {
    var psi = new ProcessStartInfo();

    psi.FileName = ZFSBinaryPath;
    psi.Arguments = command;

    using (var process = Process.Start(psi)!)
    {
      process.WaitForExit();
    }
  }

  protected static IEnumerable<string> ExecuteZFSCommandOutput(string command)
  {
    var psi = new ProcessStartInfo();

    psi.FileName = ZFSBinaryPath;
    psi.Arguments = command;
    psi.RedirectStandardOutput = true;

    using (var process = Process.Start(psi)!)
    {
      while (true)
      {
        string? line = process.StandardOutput.ReadLine();

        if (line == null)
          break;

        yield return line;
      }
    }
  }

  public IZFSSnapshot CreateSnapshot(string snapshotName)
  {
    return new ZFSSnapshot(_deviceName, snapshotName);
  }

  public static ZFSVolume FindVolume(string deviceName)
  {
    var zfs = new ZFS("dummy", "dummy");

    foreach (var volume in zfs.EnumerateVolumes())
      if (volume.DeviceName == deviceName)
        return volume;

    throw new Exception("Unable to locate ZFS volume with device name " + deviceName);
  }

  public IEnumerable<ZFSVolume> EnumerateVolumes()
  {
    foreach (string line in ExecuteZFSCommandOutput("list -Hp"))
    {
      string[] parts = line.Split('\t');

      yield return
        new ZFSVolume()
        {
          DeviceName = parts[0],
          UsedBytes = long.Parse(parts[1]),
          AvailableBytes = long.Parse(parts[2]),
          ReferencedBytes = long.Parse(parts[3]),
          MountPoint = parts[4],
        };
    }
  }
}

