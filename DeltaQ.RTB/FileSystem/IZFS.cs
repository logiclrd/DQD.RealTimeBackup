using System;
using System.Collections.Generic;

namespace DeltaQ.RTB.FileSystem
{
	public interface IZFS
	{
		public IZFS? RootInstance { get; }

		IZFSSnapshot CreateSnapshot(string snapshotName);
		ZFSVolume FindVolume(string deviceName);
		IEnumerable<ZFSVolume> EnumerateVolumes();

		string DeviceName { get; }
		string MountPoint { get; }

		IEnumerable<IZFSSnapshot> CurrentSnapshots { get; }
		int CurrentSnapshotCount { get; }
	}
}

