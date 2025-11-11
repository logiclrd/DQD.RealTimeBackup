using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.FileSystem
{
	public class ZFS : IZFS
	{
		OperatingParameters _parameters;

		IErrorLogger _errorLogger;
		ITimer _timer;

		protected string _deviceName;
		protected string _mountPoint;

		public string DeviceName => _deviceName;
		public string MountPoint => _mountPoint;

		IZFS? _rootInstance;

		public IZFS? RootInstance => _rootInstance;

		object _currentSnapshotsSync = new object();
		List<IZFSSnapshot> _currentSnapshots = new List<IZFSSnapshot>();

		public IEnumerable<IZFSSnapshot> CurrentSnapshots
		{
			get
			{
				lock (_currentSnapshotsSync)
					return new List<IZFSSnapshot>(_currentSnapshots);
			}
		}

		public int CurrentSnapshotCount
		{
			get
			{
				lock (_currentSnapshotsSync)
					return _currentSnapshots.Count;
			}
		}

		const string Dummy = "dummy";

		public ZFS(OperatingParameters parameters, IErrorLogger errorLogger, ITimer timer)
		{
			_parameters = parameters;

			_errorLogger = errorLogger;
			_timer = timer;

			_deviceName = Dummy;
			_mountPoint = Dummy;
		}

		public ZFS(OperatingParameters parameters, IErrorLogger errorLogger, ITimer timer, string deviceName, IZFS? rootInstance)
			: this(parameters, errorLogger, timer, new ZFS(parameters, errorLogger, timer).FindVolume(deviceName))
		{
			_rootInstance = rootInstance;
		}

		public ZFS(OperatingParameters parameters, IErrorLogger errorLogger, ITimer timer, string deviceName)
			: this(parameters, errorLogger, timer, new ZFS(parameters, errorLogger, timer).FindVolume(deviceName))
		{
		}

		public ZFS(OperatingParameters parameters, IErrorLogger errorLogger, ITimer timer, ZFSVolume volume, IZFS? rootInstance)
			: this(parameters, errorLogger, timer, volume)
		{
			_rootInstance = rootInstance;
		}

		public ZFS(OperatingParameters parameters, IErrorLogger errorLogger, ITimer timer, ZFSVolume volume)
			: this(
					parameters,
					errorLogger,
					timer,
					volume.DeviceName ?? throw new ArgumentNullException("volume.DeviceName"),
					volume.MountPoint ?? throw new ArgumentNullException("volume.MountPoint"))
		{
		}

		ZFS(OperatingParameters parameters, IErrorLogger errorLogger, ITimer timer, string deviceName, string mountPoint)
		{
			_parameters = parameters;

			_errorLogger = errorLogger;
			_timer = timer;

			_deviceName = deviceName;
			_mountPoint = mountPoint;
		}

		protected internal int ExecuteZFSCommand(string command)
		{
			ZFSDebugLog.WriteLine("Running: zfs {0}", command);

			var psi = new ProcessStartInfo();

			psi.FileName = _parameters.ZFSBinaryPath;
			psi.Arguments = command;
			psi.RedirectStandardOutput = true;
			psi.RedirectStandardError = true;

			using (var process = Process.Start(psi)!)
			{
				process.WaitForExit();

				return process.ExitCode;
			}
		}

		protected internal IEnumerable<string> ExecuteZFSCommandOutput(string command)
		{
			ZFSDebugLog.WriteLine("Running and capturing output: zfs {0}", command);

			var psi = new ProcessStartInfo();

			psi.FileName = _parameters.ZFSBinaryPath;
			psi.Arguments = command;
			psi.RedirectStandardOutput = true;
			psi.RedirectStandardError = true;

			using (var process = Process.Start(psi)!)
			{
				while (true)
				{
					string? line = process.StandardOutput.ReadLine();

					if (line == null)
						break;

					yield return line;
				}
			}
		}

		protected internal TOutput ExecuteZFSCommandOutput<TOutput>(string command)
			where TOutput : JSON.CommandOutput
		{
			ZFSDebugLog.WriteLine("Running and capturing output: zfs {0}", command);

			var psi = new ProcessStartInfo();

			psi.FileName = _parameters.ZFSBinaryPath;
			psi.Arguments = command;
			psi.RedirectStandardOutput = true;
			psi.RedirectStandardError = true;

			using (var process = Process.Start(psi)!)
			{
				var stdout = new UTF8FromTextReaderStream(process.StandardOutput);

				var output = JsonSerializer.Deserialize<TOutput>(
					stdout,
					JSON.Deserializer.CreateOptions());

				if (output == null)
					throw new Exception("Process output deserialized to null");

				return output;
			}
		}

		public IEnumerable<ZFSVolume> EnumerateSnapshots()
		{
			ZFSDebugLog.WriteLine("Enumerating snapshots");

			foreach (string line in ExecuteZFSCommandOutput("list -Hp -t snapshot"))
			{
				string[] parts = line.Split('\t');

				// 'zfs list' yields an entry for the pool itself, which includes a mount point but which isn't
				// the actual volume. No error is given creating snapshots on this device (named simply "bpool"
				// or "rpool") but the snapshot isn't meaningful and doesn't appear in /.zfs or /boot/.zfs.
				//
				// As far as I can tell, the only way to distinguish these entries is by the presence of
				// path separator characters in the device name. "bpool" and "rpool" and the like are to be
				// avoided.

				if (parts[0].IndexOf('/') < 0)
					continue;

				var volume =
					new ZFSVolume()
					{
						DeviceName = parts[0],
						UsedBytes = long.Parse(parts[1]),
						AvailableBytes = -1,
						ReferencedBytes = long.Parse(parts[3]),
						MountPoint = null,
					};

				ZFSDebugLog.WriteLine("- {0}", volume.DeviceName);

				yield return volume;
			}
		}

		void AddSnapshot(IZFSSnapshot snapshot)
		{
			lock (_currentSnapshotsSync)
			{
				_currentSnapshots.Add(snapshot);

				if (_rootInstance == null)
				{
					ZFSDebugLog.WriteLine("Tracking new snapshot, now tracking {0} snapshot{1}", _currentSnapshots.Count, _currentSnapshots.Count == 1 ? "" : "s");
					foreach (var trackedSnapshot in _currentSnapshots)
						ZFSDebugLog.WriteLine("=> {0}", trackedSnapshot.SnapshotName);
				}
			}

			(_rootInstance as ZFS)?.AddSnapshot(snapshot);
		}

		void RemoveSnapshot(IZFSSnapshot snapshot)
		{
			lock (_currentSnapshotsSync)
			{
				_currentSnapshots.Remove(snapshot);

				if (_rootInstance == null)
				{
					ZFSDebugLog.WriteLine("No longer tracking snapshot {0}, now tracking {1} snapshot{2}", snapshot.SnapshotName, _currentSnapshots.Count, _currentSnapshots.Count == 1 ? "" : "s");
					foreach (var trackedSnapshot in _currentSnapshots)
						ZFSDebugLog.WriteLine("=> {0}", trackedSnapshot.SnapshotName);
				}
			}

			(_rootInstance as ZFS)?.RemoveSnapshot(snapshot);
		}

		public IZFSSnapshot CreateSnapshot(string snapshotName)
		{
			if (_deviceName == Dummy)
				throw new InvalidOperationException("This ZFS instance is not attached to a specific device name.");

			ZFSDebugLog.WriteLine("Creating new snapshot {0} of device {1}", snapshotName, _deviceName);

			var snapshot = new ZFSSnapshot(_parameters, _errorLogger, _timer, _deviceName, snapshotName, _rootInstance);

			AddSnapshot(snapshot);

			snapshot.Disposed += (_, _) => RemoveSnapshot(snapshot);

			return snapshot;
		}

		public IZFSSnapshot AttachToSnapshot(string snapshotName)
		{
			if (_deviceName == Dummy)
				throw new InvalidOperationException("This ZFS instance is not attached to a specific device name.");

			ZFSDebugLog.WriteLine("Attaching to existing snapshot {0} of device {1}", snapshotName, _deviceName);

			var snapshot = new ZFSSnapshot(_parameters, _errorLogger, _timer, _deviceName, snapshotName, _rootInstance, attachExisting: true);

			AddSnapshot(snapshot);

			snapshot.Disposed += (_, _) => RemoveSnapshot(snapshot);

			return snapshot;
		}

		public ZFSVolume FindVolume(string deviceName)
		{
			foreach (var volume in EnumerateVolumes())
				if (volume.DeviceName == deviceName)
					return volume;

			throw new KeyNotFoundException("Unable to locate ZFS volume with device name " + deviceName);
		}

		public IEnumerable<ZFSVolume> EnumerateVolumes()
		{
			ZFSDebugLog.WriteLine("Enumerating volumes");

			var output = ExecuteZFSCommandOutput<JSON.List>("list -Hpj");

			foreach (var dataset in output.Datasets.Values)
			{
				if (dataset.Name == null)
					continue;

				// 'zfs list' yields an entry for the pool itself, which includes a mount point but which isn't
				// the actual volume. No error is given creating snapshots on this device (usually named simply
				// "bpool" or "rpool") but the snapshot isn't meaningful and doesn't appear in /.zfs or
				// /boot/.zfs.
				//
				// As far as I can tell, the only way to distinguish these entries is by the presence of
				// path separator characters in the device name. "bpool" and "rpool" and the like are to be
				// avoided.
				if (dataset.Name.IndexOf('/') < 0)
					continue;

				if (dataset.Type != JSON.DataSetType.FileSystem)
					continue;

				var volume =
					new ZFSVolume()
					{
						DeviceName = dataset.Name,
						UsedBytes = dataset.GetInt64Property(JSON.DataSetProperties.UsedBytes),
						AvailableBytes = dataset.GetInt64Property(JSON.DataSetProperties.AvailableBytes),
						ReferencedBytes = dataset.GetInt64Property(JSON.DataSetProperties.ReferencedBytes),
						MountPoint = dataset.GetStringProperty(JSON.DataSetProperties.MountPoint),
					};

				ZFSDebugLog.WriteLine("- {0} at {1}", volume.DeviceName, volume.MountPoint);

				yield return volume;
			}
		}
	}
}

