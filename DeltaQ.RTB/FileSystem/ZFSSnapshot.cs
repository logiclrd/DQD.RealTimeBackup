using System;
using System.IO;

namespace DeltaQ.RTB.FileSystem
{
	public class ZFSSnapshot : ZFS, IZFSSnapshot, IDisposable
	{
		string _snapshotName;
		bool _disposed;

		public event EventHandler? Disposed;

		public string SnapshotName => _snapshotName;

		public ZFSSnapshot(OperatingParameters parameters, string deviceName, string snapshotName, IZFS? rootInstance)
			: base(parameters, deviceName, rootInstance)
		{
			_snapshotName = snapshotName;

			ExecuteZFSCommand($"snapshot {_deviceName}@{_snapshotName}");
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				ExecuteZFSCommand($"destroy {_deviceName}@{_snapshotName}");
				Disposed?.Invoke(this, EventArgs.Empty);
				_disposed = true;
			}
		}

		public string BuildPath()
		{
			return Path.Combine(_mountPoint, ".zfs", "snapshot", _snapshotName);
		}
	}
}

