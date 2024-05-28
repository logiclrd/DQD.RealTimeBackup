using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DeltaQ.RTB.FileSystem
{
	public class ZFS : IZFS
	{
		OperatingParameters _parameters;

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

		public ZFS(OperatingParameters parameters)
		{
			_parameters = parameters;

			_deviceName = Dummy;
			_mountPoint = Dummy;
		}

		public ZFS(OperatingParameters parameters, string deviceName, IZFS? rootInstance)
			: this(parameters, new ZFS(parameters).FindVolume(deviceName))
		{
			_rootInstance = rootInstance;
		}

		public ZFS(OperatingParameters parameters, string deviceName)
			: this(parameters, new ZFS(parameters).FindVolume(deviceName))
		{
		}

		public ZFS(OperatingParameters parameters, ZFSVolume volume, IZFS? rootInstance)
			: this(parameters, volume)
		{
			_rootInstance = rootInstance;
		}

		public ZFS(OperatingParameters parameters, ZFSVolume volume)
			: this(
					parameters,
					volume.DeviceName ?? throw new ArgumentNullException("volume.DeviceName"),
					volume.MountPoint ?? throw new ArgumentNullException("volume.MountPoint"))
		{
		}

		ZFS(OperatingParameters parameters, string deviceName, string mountPoint)
		{
			_parameters = parameters;
			_deviceName = deviceName;
			_mountPoint = mountPoint;
		}

		protected internal void ExecuteZFSCommand(string command)
		{
			ZFSDebugLog.WriteLine("Running: zfs {0}", command);

			var psi = new ProcessStartInfo();

			psi.FileName = _parameters.ZFSBinaryPath;
			psi.Arguments = command;

			using (var process = Process.Start(psi)!)
			{
				process.WaitForExit();
			}
		}

		protected internal IEnumerable<string> ExecuteZFSCommandOutput(string command)
		{
			ZFSDebugLog.WriteLine("Running and capturing output: zfs {0}", command);

			var psi = new ProcessStartInfo();

			psi.FileName = _parameters.ZFSBinaryPath;
			psi.Arguments = command;
			psi.RedirectStandardOutput = true;

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

		void AddSnapshot(IZFSSnapshot snapshot)
		{
			lock (_currentSnapshotsSync)
			{
				_currentSnapshots.Add(snapshot);

				if (_rootInstance == null)
				{
					ZFSDebugLog.WriteLine("Tracking new snapshot, now tracking {0} snapshot{1}", _currentSnapshots.Count, _currentSnapshots.Count == 1 ? "" : "s");
					ZFSDebugLog.WriteLine("=> {0}", snapshot.SnapshotName);
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
					ZFSDebugLog.WriteLine("No longer tracking snapshot, now tracking {0} snapshot{1}", _currentSnapshots.Count, _currentSnapshots.Count == 1 ? "" : "s");
					ZFSDebugLog.WriteLine("=> {0}", snapshot.SnapshotName);
				}
			}

			(_rootInstance as ZFS)?.RemoveSnapshot(snapshot);
		}

		public IZFSSnapshot CreateSnapshot(string snapshotName)
		{
			if (_deviceName == Dummy)
				throw new InvalidOperationException("This ZFS instance is not attached to a specific device name.");

			ZFSDebugLog.WriteLine("Creating new snapshot of device {0}", _deviceName);

			var snapshot = new ZFSSnapshot(_parameters, _deviceName, snapshotName, _rootInstance);

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

			foreach (string line in ExecuteZFSCommandOutput("list -Hp"))
			{
				string[] parts = line.Split('\t');

				var volume =
					new ZFSVolume()
					{
						DeviceName = parts[0],
						UsedBytes = long.Parse(parts[1]),
						AvailableBytes = long.Parse(parts[2]),
						ReferencedBytes = long.Parse(parts[3]),
						MountPoint = parts[4],
					};

				ZFSDebugLog.WriteLine("- {0} at {1}", volume.DeviceName, volume.MountPoint);

				yield return volume;
			}
		}
	}
}

