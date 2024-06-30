using System;

namespace DQD.RealTimeBackup.FileSystem
{
	public interface IZFSSnapshot : IZFS, IDisposable
	{
		string SnapshotName { get; }

		string BuildPath();
	}
}

