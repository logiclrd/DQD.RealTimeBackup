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

		const string Dummy = "dummy";

		public ZFS(OperatingParameters parameters)
		{
			_parameters = parameters;

			_deviceName = Dummy;
			_mountPoint = Dummy;
		}

		public ZFS(OperatingParameters parameters, string deviceName)
			: this(parameters, new ZFS(parameters).FindVolume(deviceName))
		{
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

		public IZFSSnapshot CreateSnapshot(string snapshotName)
		{
			if (_deviceName == Dummy)
				throw new InvalidOperationException("This ZFS instance is not attached to a specific device name.");

			return new ZFSSnapshot(_parameters, _deviceName, snapshotName);
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
			foreach (string line in ExecuteZFSCommandOutput("list -Hp"))
			{
				string[] parts = line.Split('\t');

				yield return
					new ZFSVolume()
					{
						DeviceName = parts[0],
						UsedBytes = long.Parse(parts[1]),
						AvailableBytes = long.Parse(parts[2]),
						ReferencedBytes = long.Parse(parts[3]),
						MountPoint = parts[4],
					};
			}
		}
	}
}

