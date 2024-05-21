using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
			using (var reader = new StreamReader("/proc/self/mountinfo"))
			{
				while (true)
				{
					string? line = reader.ReadLine();

					if (line == null)
						break;

					NativeMethods.DecodeMountInfoEntry(
						line,
						out int mountID,
						out int parentMountID,
						out int deviceMajor,
						out int deviceMinor,
						out string root,
						out string mountPoint,
						out string options,
						out string[] optionalFields,
						out string fileSystemType,
						out string deviceName,
						out string? superblockOptions);

					root = Unescape(root)!;
					mountPoint = Unescape(mountPoint)!;
					options = Unescape(options)!;

					for (int i=0; i < optionalFields.LongLength; i++)
						optionalFields[i] = Unescape(optionalFields[i])!;

					deviceName = Unescape(deviceName)!;
					superblockOptions = Unescape(superblockOptions);

					yield return new Mount(mountID, parentMountID, deviceMajor, deviceMinor, root, mountPoint, options, optionalFields, fileSystemType, deviceName, superblockOptions);
				}
			}
		}

		internal static string? Unescape(string? escapedString)
		{
			if (escapedString == null)
				return null;

			int sequenceIndex = escapedString.IndexOf('\\');

			if (sequenceIndex	< 0)
				return escapedString;

			int nextIndex = 0;

			var decoded = new StringBuilder();

			while (sequenceIndex >= 0)
			{
				decoded.Append(escapedString, nextIndex, sequenceIndex - nextIndex);

				nextIndex = sequenceIndex;

				// Certain characters are encoded as \ooo, where ooo is the ASCII code in octal.
				int value =         (escapedString[++nextIndex] - '0');
				value = value * 8 + (escapedString[++nextIndex] - '0');
				value = value * 8 + (escapedString[++nextIndex] - '0');

				decoded.Append((char)value);

				nextIndex++;

				sequenceIndex = escapedString.IndexOf('\\', nextIndex);
			}

			decoded.Append(escapedString, nextIndex, escapedString.Length - nextIndex);

			return decoded.ToString();
		}
	}
}

