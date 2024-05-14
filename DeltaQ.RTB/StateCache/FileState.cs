using System;
using System.ComponentModel;
using System.IO;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.StateCache
{
	public class FileState
	{
		public string Path;
		public long FileSize;
		public DateTime LastModifiedUTC;
		public string Checksum;

		[EditorBrowsable(EditorBrowsableState.Never)]
		public FileState()
		{
			// Dummy constructor.
			Path = Checksum = "";
		}

		public static FileState FromFile(string path, IChecksum checksum)
		{
			var ret = new FileState();

			ret.Path = path;
			ret.LastModifiedUTC = File.GetLastWriteTimeUtc(path);

			using (var stream = File.OpenRead(path))
			{
				ret.FileSize = stream.Length;
				ret.Checksum = checksum.ComputeChecksum(stream);
			}

			return ret;
		}

		public bool IsMatch(IChecksum checksum)
		{
			if (!File.Exists(Path))
				return false;

			using (var stream = File.OpenRead(Path))
			{
				if (stream.Length != FileSize)
					return false;

				if (checksum.ComputeChecksum(stream) != Checksum)
					return false;

				return true;
			}
		}

		public static FileState Parse(string serialized)
		{
			string[] parts = serialized.Split(' ', 4);

			var ret = new FileState();

			ret.Path = parts[3];
			ret.LastModifiedUTC = new DateTime(ticks: long.Parse(parts[2]), DateTimeKind.Utc);
			ret.Checksum = parts[0];
			ret.FileSize = long.Parse(parts[1]);

			return ret;
		}

		public override string ToString()
		{
			if (Path.IndexOf('\n') >= 0)
				throw new Exception("Sanity failure: Path contains a newline character.");

			return $"{Checksum} {FileSize} {LastModifiedUTC.Ticks} {Path}";
		}
	}
}

