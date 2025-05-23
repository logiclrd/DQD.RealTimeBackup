using System;
using System.IO;

using DQD.RealTimeBackup.FileSystem;
using DQD.RealTimeBackup.Storage;

namespace DQD.RealTimeBackup.Agent
{
	public class FileReference : IDisposable
	{
		public string Path;
		public SnapshotReference? SnapshotReference;
		public IStagedFile? StagedFile;

		public string SourcePath;
		public long FileSize;
		public DateTime LastModifiedUTC;
		public string Checksum;

		public FileReference(SnapshotReference snapshotReference, DateTime lastModifiedUTC, string checksum)
		{
			this.Path = snapshotReference.Path;
			this.SnapshotReference = snapshotReference;
			this.SourcePath = snapshotReference.SnapshottedPath;
			this.FileSize = new FileInfo(this.SourcePath).Length;
			this.LastModifiedUTC = lastModifiedUTC;
			this.Checksum = checksum;
		}

		public FileReference(string path, IStagedFile stagedFile, DateTime lastModifiedUTC, string checksum)
		{
			this.Path = path;
			this.StagedFile = stagedFile;
			this.SourcePath = stagedFile.Path;
			this.FileSize = new FileInfo(this.SourcePath).Length;
			this.LastModifiedUTC = lastModifiedUTC;
			this.Checksum = checksum;
		}

		public override string ToString()
		{
			if (StagedFile != null)
				return Path + " (staged at: " + StagedFile.Path + ")";
			else if (SnapshotReference != null)
				return Path + " (from snapshot at: " + SnapshotReference.SnapshottedPath + ")";
			else
				return Path + " (?)";
		}

		public void Dispose()
		{
			SnapshotReference?.Dispose();
			StagedFile?.Dispose();
		}
	}
}
