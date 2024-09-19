using System;
using System.ComponentModel;
using System.IO;

using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.StateCache
{
	public class FileState
	{
		public string Path;
		public string ContentKey;
		public long FileSize;
		public DateTime LastModifiedUTC;
		public string Checksum;
		public bool IsInParts;
		public int PartNumber;

		[EditorBrowsable(EditorBrowsableState.Never)]
		public FileState()
		{
			// Dummy constructor.
			Path = ContentKey = Checksum = "";
		}

		public FileState CreatePartState(int partNumber)
		{
			this.IsInParts = true;

			return
				new FileState()
				{
					Path = this.Path,
					ContentKey = this.ContentKey,
					IsInParts = true,
					PartNumber = partNumber,
				};
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

		const string EmptyContentKeyToken = "\"\"";

		public static FileState Parse(string serialized)
		{
			var ret = new FileState();

			if (serialized.StartsWith("*"))
			{
				ret.IsInParts = true;

				int separator = serialized.IndexOf(' ');

				ret.PartNumber = int.Parse(serialized.Substring(1, separator - 1));

				serialized = serialized.Substring(separator + 1);
			}

			string[] parts = serialized.Split(' ', 5);

			ret.Path = parts[4];
			ret.ContentKey = parts[3];
			ret.LastModifiedUTC = new DateTime(ticks: long.Parse(parts[2]), DateTimeKind.Utc);
			ret.Checksum = parts[0];
			ret.FileSize = long.Parse(parts[1]);

			if (ret.ContentKey == EmptyContentKeyToken)
				ret.ContentKey = "";

			return ret;
		}

		public override string ToString()
		{
			if (Path.IndexOf('\n') >= 0)
				throw new Exception("Sanity failure: Path contains a newline character.");

			string contentKeySerialized = ContentKey;

			if (string.IsNullOrWhiteSpace(contentKeySerialized))
				contentKeySerialized = EmptyContentKeyToken;

			if (PartNumber == 0)
				return $"{(IsInParts ? $"*0 " : "")}{Checksum} {FileSize} {LastModifiedUTC.Ticks} {contentKeySerialized} {Path}";
			else
				return $"{(IsInParts ? $"*{PartNumber} " : "")}{Checksum} 0 0 0 {Path}";
		}
	}
}

