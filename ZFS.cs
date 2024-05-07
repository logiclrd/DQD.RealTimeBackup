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

  protected void ExecuteZFSCommand(string command)
  {
    var psi = new ProcessStartInfo();

    psi.FileName = ZFSBinaryPath;
    psi.Arguments = command;

    using (var process = Process.Start(psi)!)
    {
      process.WaitForExit();
    }
  }

  public ZFSSnapshot CreateSnapshot(string snapshotName)
  {
    return new ZFSSnapshot(_deviceName, snapshotName);
  }
}
