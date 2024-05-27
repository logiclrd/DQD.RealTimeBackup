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

		// For debugging, a separate file that contains detailed logging of File Access Notify operations.
		public string? FileAccessNotifyDebugLogPath = null;

		public List<string> ExcludePaths =
			new List<string>()
			{
				"/var",
				"/run",
				"/tmp",
				"/bin",
				"/lib",
				"/lib32",
				"/lib64",
				"/libx32",
				"/usr/bin",
				"/usr/lib",
				"/opt",
				"/var/lib/dpkg",
				"/var/lib/swcatalog",
			};

		public bool IsExcludedPath(string path)
		{
			// This is implemented in a slightly less readable way for performance reasons, since it will be
			// called over and over and over. We avoid constructing objects.
			//
			// Semantics:
			//   * If the path is exactly equal to an ExcludePaths member, return true.
			//   * If the path starts with an ExcludePaths member which is then immediately followed by a '/', return true.
			//   * Otherwise, return false.

			for (int i = 0; i < ExcludePaths.Count; i++)
			{
				string excluded = ExcludePaths[i];

				if (path.Length >= excluded.Length)
				{
					if ((path.Length == excluded.Length) || (path[excluded.Length] == '/'))
					{
						if (string.Compare(path, 0, excluded, 0, excluded.Length) == 0)
						{
							return true;
						}
					}
				}
			}

			return false;
		}

		// Used to resolve situations where the same device is mounted to multiple places. When this
		// happens, if one of those places is in this list, only that one will be used. If none of the
		// mounts are in this list, then an error will be generated.
		public List<string> PreferredMountPoints = new List<string>();

		// Specifies filesystem types that should be monitored.
		public List<string> MonitorFileSystemTypes = new List<string>() { "zfs" };

		// When submitting a file, if its size is less than this then it will be copied to /tmp and
		// the ZFS snapshot released. If its size is greater than or equal to this, then the ZFS
		// snapshot will be retained and used as the source for the upload.
		public long MaximumFileSizeForStagingCopy = 500 * 1048576;

		// Path to the LSOF binary.
		public string LSOFBinaryPath = "/usr/bin/lsof";

		// When a queue hits this many items, things that feed into it will pause until it drops down
		// to the low water mark.
		public int QueueHighWaterMark = 10_000;

		// When a queue hits the high water mark, things that feed into it will pause and wait until it
		// drops down to the low water mark.
		public int QueueLowWaterMark = 5_000;

		// When polling for open file handles, this much time is the delay between checks.
		public TimeSpan OpenFileHandlePollingInterval = TimeSpan.FromSeconds(4);

		// When polling for open file handles to go away, if this much time elapses then the alternate
		// strategy (long polling) for dealing with files that aren't being closed is engaged.
		public TimeSpan MaximumTimeToWaitForNoOpenFileHandles = TimeSpan.FromSeconds(30);

		// The long polling strategy checks this often for changes.
		public TimeSpan LongPollingInterval = TimeSpan.FromSeconds(30);

		// If a file sits in the long polling queue for this long, it will get uploaded as-is even though it hasn't been
		// observed to be stable yet.
		public TimeSpan MaximumLongPollingTime = TimeSpan.FromMinutes(10);

		// Path to the ZFS binary.
		public string ZFSBinaryPath = "/usr/sbin/zfs";

		// After a file update event, this much time will be permitted to elapse to allow other file
		// update events to take place, so that a single ZFS snapshot can capture new versions of
		// multiple files.
		public TimeSpan SnapshotSharingWindow = TimeSpan.FromSeconds(5);

		// Local path at which the remote file state cache is stored, persisting it across runs.
		public string RemoteFileStateCachePath = "/var/DeltaQ.RTB/FileStateCache";

		// For debugging, a separate file that contains detailed logging of Remote File State Cache operations.
		public string? RemoteFileStateCacheDebugLogPath = null;

		// Delay after a first file state update before the current file is pinched off and uploaded,
		// to consolidate multiple updates.
		public TimeSpan BatchUploadConsolidationDelay = TimeSpan.FromSeconds(30);

		// Number of concurrent upload processing threads.
		public int UploadThreadCount = 4;

		// Interval between periodic rescans. These are disabled while an initial backup is in progress.
		public TimeSpan PeriodicRescanInterval = TimeSpan.FromHours(6);

		// Remote storage credentials.
		public string RemoteStorageKeyID = "MUST BE SET IN CONFIGURATION FILE";
		public string RemoteStorageApplicationKey = "MUST BE SET IN CONFIGURATION FILE";

		// Bucket name within remote storage.
		public string RemoteStorageBucketID = "MUST BE SET IN CONFIGURATION FILE";

		// B2-specific: Files above this threshold will be uploaded in chunks.
		public int B2LargeFileThreshold = 10 * 1048576;

		// B2-specific: Files uploaded in chunks will use this chunk size.
		public int B2LargeFileChunkSize = 5 * 1048576;
	}
}

