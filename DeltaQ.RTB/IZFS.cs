using System;
using System.Collections.Generic;

public interface IZFS
{
  IZFSSnapshot CreateSnapshot(string snapshotName);
  IEnumerable<ZFSVolume> EnumerateVolumes();

  string DeviceName { get; }
  string MountPoint { get; }
}

