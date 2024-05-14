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

		public FileReference(SnapshotReference snapshotReference, Stream stream)
		{
			this.Path = snapshotReference.Path;
			this.SnapshotReference = snapshotReference;
			this.Stream = stream;
		}

		public FileReference(string path, IStagedFile stagedFile)
		{
			this.Path = stagedFile.Path;
			this.StagedFile = stagedFile;
			this.Stream = File.OpenRead(stagedFile.Path);
		}

		public void Dispose()
		{
			SnapshotReference?.Dispose();
			StagedFile?.Dispose();

			Stream?.Dispose();
		}
	}
}
