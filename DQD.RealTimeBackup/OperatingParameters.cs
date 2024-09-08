using System;
using System.Collections.Generic;

using DQD.RealTimeBackup.SurfaceArea;

namespace DQD.RealTimeBackup
{
	public class OperatingParameters
	{
		// Output verbosity level. Can be raised for debugging purposes.
		public Verbosity Verbosity = Verbosity.Normal;

		public bool Quiet => Verbosity < Verbosity.Normal;
		public bool Verbose => Verbosity > Verbosity.Normal;

		public const string DefaultErrorLogFilePath = "/var/log/DQD.RealTimeBackup.error.log";

		// Path to which particularly important errors are written.
		public string ErrorLogFilePath = DefaultErrorLogFilePath;

		public const string DefaultIPCPath = "/run/DQD.RealTimeBackup";

		// Path in which IPC parameters are stashed, and subsequently read by interface processes.
		public string IPCPath = DefaultIPCPath;

		// If true, the Bridge server will listen for connections in UNIX domain sockets. The socket
		// will be named "bridge.socket" in the folder identified by IPCPath.
		public bool IPCUseUNIXSocket = true;

		// If true, the Bridge server will listen for TCP/IP connections. The TCP port number will
		// be stored in a file named "bridge-tcp-port" in the folder identified by IPCPath.
		public bool IPCUseTCPSocket = false;

		// Allows you to bind the TCP endpoint for IPC to an address other than localhost. This can
		// allow a remote user interface. Consider security implications before doing this.
		public string IPCBindTCPAddress = "127.0.0.1";

		// Allows the port number for the TCP endpoint to be fixed. By default it is dynamically-assigned.
		public int IPCBindTCPPortNumber = 0;

		// Set to false to run a BackupAgent that responds only to explicit notifications. For instance,
		// this is used to achieve initial backup.
		public bool EnableFileAccessNotify = true;

		// For debugging, a separate file that contains detailed logging of File Access Notify operations.
		public string? FileAccessNotifyDebugLogPath = null;

		public List<PathFilter> PathFilters =
			new List<PathFilter>()
			{
				PathFilter.ExcludePrefix("/var"),
				PathFilter.ExcludePrefix("/run"),
				PathFilter.ExcludePrefix("/tmp"),
				PathFilter.ExcludePrefix("/bin"),
				PathFilter.ExcludePrefix("/lib"),
				PathFilter.ExcludePrefix("/lib32"),
				PathFilter.ExcludePrefix("/lib64"),
				PathFilter.ExcludePrefix("/libx32"),
				PathFilter.ExcludePrefix("/usr/bin"),
				PathFilter.ExcludePrefix("/usr/lib"),
				PathFilter.ExcludePrefix("/usr/include"),
				PathFilter.ExcludePrefix("/usr/src"),
				PathFilter.ExcludePrefix("/usr/share"),
				PathFilter.ExcludePrefix("/opt"),
				PathFilter.ExcludePrefix("/var/lib/dpkg"),
				PathFilter.ExcludePrefix("/var/lib/swcatalog"),
				PathFilter.ExcludeComponent(".cache"),
				PathFilter.ExcludeComponent(".mozilla"),
				PathFilter.ExcludeComponent("GUICache"),
				PathFilter.ExcludeComponent("DawnCache"),
				PathFilter.ExcludeComponent(".git"),
				PathFilter.ExcludeRegex("~/.config/Code/"),
			};

		public bool IsExcludedPath(string path)
		{
			foreach (var filter in PathFilters)
				if (filter.Matches(path))
					return filter.ShouldExclude;

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
		public string RemoteFileStateCachePath = "/var/DQD.RealTimeBackup/FileStateCache";

		// For debugging, a separate file that contains detailed logging of Remote File State Cache operations.
		public string? RemoteFileStateCacheDebugLogPath = null;

		// Delay after a first file state update before the current file is pinched off and uploaded,
		// to consolidate multiple updates.
		public TimeSpan BatchUploadConsolidationDelay = TimeSpan.FromSeconds(30);

		// Number of concurrent upload processing threads.
		public int UploadThreadCount = 4;

		// Interval between periodic rescans. These are disabled while an initial backup is in progress.
		public TimeSpan PeriodicRescanInterval = TimeSpan.FromHours(6);

		// Number of characters in the random alphanumeric content keys used to make the uploaded files indirect.
		// Space of unique content keys will be lg(ContentKeyAlphabet.Length) * ContentKeyLength bits.
		// For ContentKeyLength 64 and ContentKeyAlphabet with digits and letters (upper and lower), the space
		// is about 256 bits long. This is more than twice the number of meaningful bits in the best GUID.
		public int ContentKeyLength = 64;

		// Unique character set from which to draw the characters of a content key.
		public string ContentKeyAlphabet = "0123456789abcdefghijklmnopqrstuvxyzABCDEFGHIJKLMNOPQRSTUVXYZ";

		// Remote storage credentials.
		public string RemoteStorageKeyID = "MUST BE SET IN CONFIGURATION FILE";
		public string RemoteStorageApplicationKey = "MUST BE SET IN CONFIGURATION FILE";

		// Bucket name within remote storage.
		public string RemoteStorageBucketID = "MUST BE SET IN CONFIGURATION FILE";

		// B2-specific: Files above this threshold will be uploaded in chunks.
		public int B2LargeFileThreshold = 10 * 1048576;

		// B2-specific: Files uploaded in chunks will use this chunk size.
		public int B2LargeFileChunkSize = 5 * 1048576;

		// Hash of the password for web access via DQD.RealTimeBackup.Web.
		public string WebAccessPasswordHash = "MUST BE SET IN CONFIGURATION FILE";
	}
}

