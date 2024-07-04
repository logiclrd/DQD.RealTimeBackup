using System;
using System.IO;
using System.Threading.Tasks;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.FileSystem
{
	public class ZFSSnapshot : ZFS, IZFSSnapshot, IDisposable
	{
		IErrorLogger _errorLogger;
		ITimer _timer;

		string _snapshotName;
		bool _disposed;

		public event EventHandler? Disposed;

		public string SnapshotName => _snapshotName;

		public ZFSSnapshot(OperatingParameters parameters, IErrorLogger errorLogger, ITimer timer, string deviceName, string snapshotName, IZFS? rootInstance)
			: base(parameters, errorLogger, timer, deviceName, rootInstance)
		{
			_errorLogger = errorLogger;
			_timer = timer;

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
			ZFSDebugLog.WriteLine($"Destroying snapshot {_deviceName}@{_snapshotName} (previous failed attempts {failedAttempts})");

			if (!_disposed)
			{
				int exitCode = ExecuteZFSCommand($"destroy {_deviceName}@{_snapshotName}");

				ZFSDebugLog.WriteLine("Exit code: {0}", exitCode);

				if (exitCode != 0)
				{
					failedAttempts++;

					if (failedAttempts < MaximumDisposeRetries)
					{
						ZFSDebugLog.WriteLine("=> Scheduling retry in {0} seconds", RetryIntervalSeconds);

						_timer.ScheduleAction(
							TimeSpan.FromSeconds(RetryIntervalSeconds),
							() => DisposeRetry(failedAttempts));

						return;
					}
					else
					{
						_errorLogger.LogError(
							"Failed to remove ZFS snapshot",
							$"ZFS snapshot named '{_deviceName}@{_snapshotName}' could not be removed. " +
							"Retries have been exhausted, it will need to be removed manually.");
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

