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
		bool _disposing;
		bool _disposed;

		public event EventHandler? Disposed;

		public string SnapshotName => _snapshotName;

		public ZFSSnapshot(OperatingParameters parameters, IErrorLogger errorLogger, ITimer timer, string deviceName, string snapshotName, IZFS? rootInstance, bool attachExisting = false)
			: base(parameters, errorLogger, timer, deviceName, rootInstance)
		{
			_errorLogger = errorLogger;
			_timer = timer;

			_snapshotName = snapshotName;

			if (!attachExisting)
				ExecuteZFSCommand($"snapshot {_deviceName}@{_snapshotName}");
		}

		public void Dispose()
		{
			lock (this)
			{
				if (!_disposed && !_disposing)
				{
					_disposing = true;
					DisposeRetry(0, 5);
				}
			}
		}

		const int RetryIntervalSeconds = 10;

		void DisposeRetry(int failedAttempts, int nextWarningFailedAttempts)
		{
			ZFSDebugLog.WriteLine($"Destroying snapshot {_deviceName}@{_snapshotName} (previous failed attempts {failedAttempts})");

			if (!_disposed)
			{
				int exitCode = ExecuteZFSCommand($"destroy {_deviceName}@{_snapshotName}");

				ZFSDebugLog.WriteLine("Exit code: {0}", exitCode);

				if (exitCode == 0)
				{
					Disposed?.Invoke(this, EventArgs.Empty);
					_disposed = true;
					_disposing = false;
				}
				else
				{
					failedAttempts++;

					if (failedAttempts == nextWarningFailedAttempts)
					{
						_errorLogger.LogError(
							"Failed to remove ZFS snapshot",
							$"ZFS snapshot named '{_deviceName}@{_snapshotName}' could not be removed. " +
							$"Removal has been attempted {failedAttempts} times. If it cannot be removed " +
							"automatically, it may need to be removed manually. DQD.RealTimeBackup will " +
							"continue to try removing this ZFS snapshot.");

						nextWarningFailedAttempts += failedAttempts + 5;
					}

					ZFSDebugLog.WriteLine("=> Scheduling retry in {0} seconds", RetryIntervalSeconds);

					_timer.ScheduleAction(
						TimeSpan.FromSeconds(RetryIntervalSeconds),
						() => DisposeRetry(failedAttempts, nextWarningFailedAttempts));
				}
			}
		}

		public string BuildPath()
		{
			return Path.Combine(_mountPoint, ".zfs", "snapshot", _snapshotName);
		}
	}
}

