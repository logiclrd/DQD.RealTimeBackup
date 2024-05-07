using System;
using System.Diagnostics;

class ZFS
{
  const string ZFSBinaryPath = "/usr/sbin/zfs";

  protected string _deviceName;

  public ZFS(string deviceName)
  {
    _deviceName = deviceName;
  }

  public ZFS(ZFSVolume volume)
    : this(volume.DeviceName ?? throw new ArgumentNullException("volume.DeviceName"))
  {
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

  public ZFSSnapshot CreateSnapshot(string snapshotName)
  {
    return new ZFSSnapshot(_deviceName, snapshotName);
  }

  public static IEnumerable<ZFSVolume> EnumerateVolumes()
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

