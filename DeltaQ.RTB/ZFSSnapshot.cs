using System;
using System.IO;

public class ZFSSnapshot : ZFS, IZFSSnapshot, IDisposable
{
  string _snapshotName;
  bool _disposed;

  public string SnapshotName => _snapshotName;

  public ZFSSnapshot(string deviceName, string snapshotName)
    : base(deviceName)
  {
    _snapshotName = snapshotName;

    ExecuteZFSCommand($"snapshot {_deviceName}@{_snapshotName}");
  }

  public void Dispose()
  {
    if (!_disposed)
    {
      ExecuteZFSCommand($"destroy {_deviceName}@{_snapshotName}");
      _disposed = true;
    }
  }

  public string BuildPath()
  {
    return Path.Combine(_mountPoint, ".zfs", "snapshot", _snapshotName);
  }
}
