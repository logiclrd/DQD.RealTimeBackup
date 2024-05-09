using System;

public interface IZFSSnapshot : IDisposable
{
  string SnapshotName { get; }

  string BuildPath();
}
