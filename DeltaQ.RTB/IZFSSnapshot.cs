using System;

namespace DeltaQ.RTB
{
  public interface IZFSSnapshot : IDisposable
  {
    string SnapshotName { get; }

    string BuildPath();
  }
}

