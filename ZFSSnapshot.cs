class ZFSSnapshot : ZFS, IDisposable
{
  string _snapshotName;
  bool _disposed;

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
}
