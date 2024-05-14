using System;
using System.Collections.Generic;

namespace DeltaQ.RTB.Interop
{
	public class MountTable : IMountTable
	{
		public IMountHandle OpenMountForFileSystem(string mountPointPath)
		{
			int fd = NativeMethods.open(mountPointPath, NativeMethods.O_RDONLY | NativeMethods.O_NOFOLLOW);

			if (fd < 0)
				throw new Exception($"Failed to open mount point: {mountPointPath}");

			return new MountHandle(fd, mountPointPath);
		}

		public IEnumerable<IMount> EnumerateMounts()
		{
			IntPtr mounts = NativeMethods.setmntent("/proc/self/mounts", "r");
			if (mounts == IntPtr.Zero)
				throw new Exception("setmntent failed");

			IntPtr mount = NativeMethods.getmntent(mounts);

			try
			{
				while (mount != IntPtr.Zero)
				{
					NativeMethods.DecodeMountEntry(mount, out string mnt_fsname, out string mnt_dir, out string? mnt_type, out string? mnt_opts, out int mnt_freq, out int mnt_passno);

					yield return new Mount(mnt_fsname, mnt_dir, mnt_type, mnt_opts, mnt_freq, mnt_passno);

					mount = NativeMethods.getmntent(mounts);
				}
			}
			finally
			{
				NativeMethods.endmntent(mounts);
			}
		}
	}
}

