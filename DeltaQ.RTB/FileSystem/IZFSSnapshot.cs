using System;

namespace DeltaQ.RTB.FileSystem
{
	public interface IZFSSnapshot : IZFS, IDisposable
	{
		string SnapshotName { get; }

		string BuildPath();
	}
}

