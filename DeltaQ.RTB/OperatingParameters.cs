using System;
using System.Collections.Generic;

namespace DeltaQ.RTB
{
	public class OperatingParameters
	{
		// Output verbosity level. Can be raised for debugging purposes.
		public Verbosity Verbosity = Verbosity.Normal;

		public bool Quiet => Verbosity < Verbosity.Normal;
		public bool Verbose => Verbosity > Verbosity.Normal;

		// Set to false to run a BackupAgent that responds only to explicit notifications. For instance,
		// this is used to achieve initial backup.
		public bool EnableFileAccessNotify = true;

		// Used to resolve situations where the same device is mounted to multiple places. When this
		// happens, if one of those places is in this list, only that one will be used. If none of the
		// mounts are in this list, then an error will be generated.
		public List<string> PreferredMountPoints = new List<string>();

		// Specifies filesystem types that should be monitored.
		public List<string> MonitorFileSystemTypes = new List<string>() { "zfs", "ext4", "ext3", "ext2", "ext", "reiserfs", "minix", "xfs", "jfs", "xiafs", "msdos", "umsdos", "vfat", "ntfs", "sysv" };

		// When submitting a file, if its size is less than this then it will be copied to /tmp and
		// the ZFS snapshot released. If its size is greater than or equal to this, then the ZFS
		// snapshot will be retained and used as the source for the upload.
		public long MaximumFileSizeForStagingCopy = 500 * 1048576;

		// Path to the LSOF binary.
		public string LSOFBinaryPath = "/usr/bin/lsof";

		// When polling for open file handles, this much time is the delay between checks.
		public TimeSpan OpenFileHandlePollingInterval = TimeSpan.FromSeconds(4);

		// When polling for open file handles to go away, if this much time elapses then the alternate
		// strategy for dealing with files that aren't being closed is engaged.
		public TimeSpan MaximumTimeToWaitForNoOpenFileHandles = TimeSpan.FromSeconds(30);

		// Path to the ZFS binary.
		public string ZFSBinaryPath = "/usr/sbin/zfs";

		// After a file update event, this much time will be permitted to elapse to allow other file
		// update events to take place, so that a single ZFS snapshot can capture new versions of
		// multiple files.
		public TimeSpan SnapshotSharingWindow = TimeSpan.FromSeconds(5);

		// Local path at which the remote file state cache is stored, persisting it across runs.
		public string RemoteFileStateCachePath = "/var/DeltaQ.RTB/FileStateCache";

		// Delay after a first file state update before the current file is pinched off and uploaded,
		// to consolidate multiple updates.
		public TimeSpan BatchUploadConsolidationDelay = TimeSpan.FromSeconds(30);

		// Number of concurrent upload processing threads.
		public int UploadThreadCount = 4;

		// Bucket name within remote storage.
		public string RemoteStorageBucketID = "MUST BE SET IN CONFIGURATION FILE";
	}
}

