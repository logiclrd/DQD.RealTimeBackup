using System;

public interface IZFS
{
  IZFSSnapshot CreateSnapshot(string snapshotName);
  IEnumerable<ZFSVolume> EnumerateVolumes();
}

