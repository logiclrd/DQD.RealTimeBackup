using System;

namespace DeltaQ.RTB.FileSystem
{
  public class SnapshotReference : IDisposable
  {
    SnapshotReferenceTracker? _tracker;
    string _path;

    public SnapshotReference(SnapshotReferenceTracker tracker, string path)
    {
      _tracker = tracker;
      _path = path;
    }

    public void Dispose()
    {
      _tracker?.Release();
      _tracker = null;
    }

    public string Path => _path;
    public string SnapshottedPath => System.IO.Path.Combine(_tracker?.Snapshot.BuildPath() ?? "", _path.TrimStart('/'));
  }
}

