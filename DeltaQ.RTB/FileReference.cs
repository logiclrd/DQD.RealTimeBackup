using System;
using System.IO;

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
    this.Path = path;
    this.StagedFile = stagedFile;
    this.Stream = File.OpenRead(stagedFile.Path);
  }

  public void Dispose()
  {
    SnapshotReference?.Dispose();
    StagedFile?.Dispose();

    Stream.Dispose();
  }
}


