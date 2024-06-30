using System;
using System.Threading;

using DQD.RealTimeBackup.Diagnostics;

namespace DQD.RealTimeBackup.FileSystem
{
	public class SnapshotReferenceTracker
	{
		public readonly IZFSSnapshot Snapshot;
		public int ReferenceCount;

		static int s_nextID = 0;

		IErrorLogger _errorLogger;

		int _id;

		public SnapshotReferenceTracker(IZFSSnapshot snapshot, IErrorLogger errorLogger)
		{
			_id = Interlocked.Increment(ref s_nextID);

			ZFSDebugLog.WriteLine("[{0}] Snapshot reference tracker set up for snapshot: {1}", _id, snapshot.SnapshotName);

			this.Snapshot = snapshot;

			_errorLogger = errorLogger;
		}

		public SnapshotReference AddReference(string path)
		{
			ZFSDebugLog.WriteLine("[{0}] Added reference for path: {1}", _id, path);

			Interlocked.Increment(ref ReferenceCount);

			ZFSDebugLog.WriteLine("[{0}] Reference count is now {1}", _id, ReferenceCount);

			return new SnapshotReference(this, path, _errorLogger);
		}

		public void Release(SnapshotReference reference)
		{
			ZFSDebugLog.WriteLine("[{0}] Releasing reference for path: {1}", _id, reference.Path);

			Interlocked.Decrement(ref ReferenceCount);

			ZFSDebugLog.WriteLine("[{0}] Reference count is now {1}", _id, ReferenceCount);

			if (ReferenceCount == 0)
			{
				ZFSDebugLog.WriteLine("[{0}] => Disposing of the snapshot", _id);
				Snapshot.Dispose();
			}
		}
	}
}

