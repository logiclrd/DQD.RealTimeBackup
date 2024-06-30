using System;
using System.IO;
using System.Threading.Tasks;

using DQD.RealTimeBackup.Diagnostics;

namespace DQD.RealTimeBackup.FileSystem
{
	public class ZFSSnapshot : ZFS, IZFSSnapshot, IDisposable
	{
		string _snapshotName;
		bool _disposed;

		public event EventHandler? Disposed;

		public string SnapshotName => _snapshotName;

		public ZFSSnapshot(OperatingParameters parameters, IErrorLogger errorLogger, string deviceName, string snapshotName, IZFS? rootInstance)
			: base(parameters, errorLogger, deviceName, rootInstance)
		{
			_snapshotName = snapshotName;

			ExecuteZFSCommand($"snapshot {_deviceName}@{_snapshotName}");
		}

		public void Dispose()
		{
			DisposeRetry(0);
		}

		const int MaximumDisposeRetries = 5;
		const int RetryIntervalSeconds = 10;

		void DisposeRetry(int failedAttempts)
		{
			if (!_disposed)
			{
				int exitCode = ExecuteZFSCommand($"destroy {_deviceName}@{_snapshotName}");

				if (exitCode != 0)
				{
					failedAttempts++;

					if (failedAttempts < MaximumDisposeRetries)
					{
						Task
							.Delay(TimeSpan.FromSeconds(RetryIntervalSeconds))
							.ContinueWith(t => DisposeRetry(failedAttempts));
					}
					else
					{
					}
				}

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

