using System;
using System.Collections.Generic;

namespace DQD.RealTimeBackup.FileSystem
{
	public interface IZFS
	{
		IZFS? RootInstance { get; }

		IZFSSnapshot CreateSnapshot(string snapshotName);
		IZFSSnapshot AttachToSnapshot(string snapshotName);
		ZFSVolume FindVolume(string deviceName);
		IEnumerable<ZFSVolume> EnumerateVolumes();
		IEnumerable<ZFSVolume> EnumerateSnapshots();

		string DeviceName { get; }
		string MountPoint { get; }

		IEnumerable<IZFSSnapshot> CurrentSnapshots { get; }
		int CurrentSnapshotCount { get; }
	}
}

