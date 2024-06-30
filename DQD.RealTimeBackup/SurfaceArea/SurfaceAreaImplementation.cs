using System;
using System.Collections.Generic;
using System.Linq;

using DQD.RealTimeBackup.Interop;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.SurfaceArea
{
	public class SurfaceAreaImplementation : DiagnosticOutputBase, ISurfaceArea
	{
		OperatingParameters _parameters;
		IMountTable _mountTable;

		List<IMount> _mounts = new List<IMount>();

		public IEnumerable<IMount> Mounts => _mounts;

		public SurfaceAreaImplementation(OperatingParameters parameters, IMountTable mountTable)
		{
			_parameters = parameters;
			_mountTable = mountTable;
		}

		public void ClearMounts()
		{
			_mounts.Clear();
		}

		public void AddMount(IMount mount)
		{
			_mounts.Add(mount);
		}

		public void AddMounts(IEnumerable<IMount> mounts)
		{
			_mounts.AddRange(mounts);
		}

		public void BuildDefault()
		{
			ClearMounts();

			var allMounts = _mountTable.EnumerateMounts().ToList();

			var mountsByDevice = allMounts
				.GroupBy(key => key.DeviceName)
				.Select(grouping => grouping.ToList())
				.ToList();

			foreach (var mountSet in mountsByDevice)
			{
				for (int i = mountSet.Count - 1; i >= 0; i--)
				{
					if (!string.IsNullOrWhiteSpace(mountSet[i].Root) && (mountSet[i].Root != "/"))
					{
						OnDiagnosticOutput("Discarding mount because it isn't at root: {0}[{1}] @ {2}", mountSet[i].DeviceName, mountSet[i].Root, mountSet[i].MountPoint);
						mountSet.RemoveAt(i);
						continue;
					}

					if (!_parameters.MonitorFileSystemTypes.Contains(mountSet[i].FileSystemType))
					{
						OnDiagnosticOutput("Discarding mount because of the filesystem type: {0}[{1}] @ {2}", mountSet[i].DeviceName, mountSet[i].Root, mountSet[i].MountPoint);
						mountSet.RemoveAt(i);
						continue;
					}
				}

				if (mountSet.Count > 1)
				{
					int preferredMountPointIndex = -1;

					for (int i = mountSet.Count - 1; i >= 0; i--)
					{
						if (_parameters.PreferredMountPoints.Contains(mountSet[i].MountPoint))
						{
							if (preferredMountPointIndex >= 0)
								throw new Exception($"More than one mount point refers to device {mountSet[0].DeviceName}. One of these needs to be explicitly selected using the PreferredMountPoint option. Presently, more than one is selected.");

							preferredMountPointIndex = i;
						}
					}

					if (preferredMountPointIndex < 0)
						throw new Exception($"More than one mount point refers to device {mountSet[0].DeviceName}. One of these needs to be explicitly selected using the PreferredMountPoint option. Presently, none is selected.");

					var preferredMount = mountSet[preferredMountPointIndex];

					mountSet.Clear();
					mountSet.Add(preferredMount);
				}
			}

			AddMounts(mountsByDevice.SelectMany(m => m));

			for (int i = _mounts.Count - 1; i >= 0; i--)
			{
				var mount = _mounts[i];

				if (mount.FileSystemType != "zfs")
				{
					if (!mount.TestDeviceAccess() || !mount.DeviceName.StartsWith("/"))
					{
						OnDiagnosticOutput("Discarding mount because its device is inaccessible: {0}[{1}] @ {2}", mount.DeviceName, mount.Root, mount.MountPoint);
						_mounts.RemoveAt(i);
						continue;
					}
				}

				if (string.IsNullOrWhiteSpace(mount.MountPoint))
				{
					OnDiagnosticOutput("Discarding mount because its mount point is empty: {0}[{1}] @ {2}", mount.DeviceName, mount.Root, mount.MountPoint);
					_mounts.RemoveAt(i);
					continue;
				}
			}

			OnDiagnosticOutput("Sorting mounts");

			_mounts.Sort(
				(x, y) =>
				{
					bool xIsHome = x.MountPoint.StartsWith("/home");
					bool yIsHome = y.MountPoint.StartsWith("/home");

					if (xIsHome != yIsHome)
						return -xIsHome.CompareTo(yIsHome);
					else
						return x.MountPoint.CompareTo(y.MountPoint);
				});
		}
	}
}
