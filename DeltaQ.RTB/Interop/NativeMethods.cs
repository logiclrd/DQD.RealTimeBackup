using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DeltaQ.RTB.Interop
{
	class NativeMethods
	{
		public const int MAX_PATH = 4096;

		[StructLayout(LayoutKind.Sequential)]
		public struct PollFD
		{
			public int FileDescriptor;
			public short RequestedEvents;
			public short ReturnedEvents;
		}

		public const int EventHeaderLength = 24;

		public const int FAN_MARK_ADD = 1;
		public const int FAN_MARK_FILESYSTEM = 256;

		public const int AT_FDCWD = -100;

		public const int O_RDONLY = 0;
		public const int O_NOFOLLOW = 131072;
		public const int O_NONBLOCK = 2048;
		public const int O_LARGEFILE = 0;
		public const int O_PATH = 0x00200000;

		public const int F_OK = 0;

		public const int POLLIN = 1;

		public const int INFTIM = -1;

		[DllImport("c", SetLastError = true)]
		public static extern int fanotify_init(FileAccessNotifyFlags flags, int event_flags);
		[DllImport("c", SetLastError = true)]
		public static extern int fanotify_mark(IntPtr fanotify_fd, int flags, long mask, int dirfd, string pathname);
		[DllImport("c", SetLastError = true)]
		public static extern int posix_memalign(ref IntPtr memptr, IntPtr alignment, IntPtr size);
		[DllImport("c", SetLastError = true)]
		public static extern int access(string pathname, int mode);
		[DllImport("c", SetLastError = true)]
		public static extern int pipe(int[] pipeFDs);
		[DllImport("c", SetLastError = true)]
		public static extern int open(string pathname, int flags);
		[DllImport("c", SetLastError = true)]
		public static extern int open_by_handle_at(int dirfd, byte[] handle, int flags);
		[DllImport("c", SetLastError = true)]
		public static extern int poll(PollFD[] fds, int nfds, int timeout);
		[DllImport("c", SetLastError = true)]
		public static extern int read(int fd, IntPtr buf, IntPtr count);
		[DllImport("c", SetLastError = true)]
		public static extern int write(int fd, byte[] buf, IntPtr count);
		[DllImport("c", SetLastError = true)]
		public static extern int close(int fd);
		[DllImport("c", SetLastError = true)]
		public static extern IntPtr readlink(string pathname, StringBuilder buf, int bufsiz);
		[DllImport("c", SetLastError = true)]
		public static extern int fstatfs(int fd, byte[] buf);

		public static void DecodeMountInfoEntry(string serialized, out int mountID, out int parentMountID, out int deviceMajor, out int deviceMinor, out string root, out string mountPoint, out string options, out string[] optionalFields, out string fileSystemType, out string deviceName, out string? superblockOptions)
		{
			var fields = serialized.Split(' ');

			if (fields.Length < 9)
				throw new Exception("Invalid mountinfo format: record has fewer than 9 fields");

			int i = 0;

			mountID = int.Parse(fields[i++]);
			parentMountID = int.Parse(fields[i++]);

			var deviceNumbers = fields[i++].Split(':');

			deviceMajor = int.Parse(deviceNumbers[0]);
			deviceMinor = int.Parse(deviceNumbers[1]);

			root = fields[i++];
			mountPoint = fields[i++];
			options = fields[i++];

			var optionalFieldList = new List<string>();

			while (fields[i] != "-")
				optionalFieldList.Add(fields[i++]);
			
			i++; // Skip the '-' terminator.

			optionalFields = optionalFieldList.ToArray();

			if (fields.Length - i < 2)
				throw new Exception("Invalid mountinfo format: record does not have the device name field");

			fileSystemType = fields[i++];
			deviceName = fields[i++];

			if (i < fields.Length)
				superblockOptions = fields[i++];
			else
				superblockOptions = default;
		}
	}
}

