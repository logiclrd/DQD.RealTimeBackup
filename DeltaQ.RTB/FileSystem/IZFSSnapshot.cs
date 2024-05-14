using System;

namespace DeltaQ.RTB.FileSystem
{
	public interface IZFSSnapshot : IDisposable
	{
		string SnapshotName { get; }

		string BuildPath();
	}
}

