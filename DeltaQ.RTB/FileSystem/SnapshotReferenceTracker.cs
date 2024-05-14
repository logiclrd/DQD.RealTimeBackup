using System.Threading;

namespace DeltaQ.RTB.FileSystem
{
  public class SnapshotReferenceTracker
  {
    public readonly IZFSSnapshot Snapshot;
    public int ReferenceCount;

    public SnapshotReferenceTracker(IZFSSnapshot snapshot)
    {
      this.Snapshot = snapshot;
    }

    public SnapshotReference AddReference(string path)
    {
      Interlocked.Increment(ref ReferenceCount);

      return new SnapshotReference(this, path);
    }

    public void Release()
    {
      Interlocked.Decrement(ref ReferenceCount);

      if (ReferenceCount == 0)
        Snapshot.Dispose();
    }
  }
}

