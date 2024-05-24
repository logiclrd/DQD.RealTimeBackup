using System;
using System.IO;

using DeltaQ.RTB.FileSystem;
using DeltaQ.RTB.Storage;

namespace DeltaQ.RTB.Agent
{
	public class FileReference : IDisposable
	{
		public string Path;
		public SnapshotReference? SnapshotReference;
		public IStagedFile? StagedFile;

		public Stream Stream;
		public DateTime LastModifiedUTC;
		public string Checksum;

		public FileReference(SnapshotReference snapshotReference, Stream stream, DateTime lastModifiedUTC, string checksum)
		{
			this.Path = snapshotReference.Path;
			this.SnapshotReference = snapshotReference;
			this.Stream = stream;
			this.LastModifiedUTC = lastModifiedUTC;
			this.Checksum = checksum;
		}

		public FileReference(string path, IStagedFile stagedFile, DateTime lastModifiedUTC, string checksum)
		{
			this.Path = path;
			this.StagedFile = stagedFile;
			this.Stream = File.OpenRead(stagedFile.Path);
			this.LastModifiedUTC = lastModifiedUTC;
			this.Checksum = checksum;
		}

		public void Dispose()
		{
			SnapshotReference?.Dispose();
			StagedFile?.Dispose();

			Stream?.Dispose();
		}
	}
}
