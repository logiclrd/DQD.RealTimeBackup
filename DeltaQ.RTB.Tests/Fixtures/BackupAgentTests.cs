using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using NUnit.Framework;

using Bogus;
using AutoBogus;

using NSubstitute;

using FluentAssertions;

using DeltaQ.RTB.Tests.Support;

namespace DeltaQ.RTB.Tests.Fixtures
{
  [TestFixture]
  public class BackupAgentTests
  {
    Faker _faker = new Faker();

    BackupAgent CreateSUT(OperatingParameters parameters, out ITimer timer, out IChecksum checksum, out IFileSystemMonitor monitor, out IOpenFileHandles openFileHandles, out IZFS zfs, out IStaging staging, out IRemoteFileStateCache remoteFileStateCache, out IRemoteStorage storage)
    {
      timer = Substitute.For<ITimer>();
      checksum = Substitute.For<IChecksum>();
      monitor = Substitute.For<IFileSystemMonitor>();
      openFileHandles = Substitute.For<IOpenFileHandles>();
      zfs = Substitute.For<IZFS>();
      staging = Substitute.For<IStaging>();
      remoteFileStateCache = Substitute.For<IRemoteFileStateCache>();
      storage = Substitute.For<IRemoteStorage>();

      var md5Checksum = new MD5Checksum();

      checksum.ComputeChecksum(Arg.Any<Stream>()).Returns(x => md5Checksum.ComputeChecksum(x.Arg<Stream>()));
      checksum.ComputeChecksum(Arg.Any<string>()).Returns(x => md5Checksum.ComputeChecksum(new MemoryStream(Encoding.UTF8.GetBytes(x.Arg<string>()))));

      return new BackupAgent(
        parameters,
        timer,
        checksum,
        monitor,
        openFileHandles,
        zfs,
        staging,
        remoteFileStateCache,
        storage);
    }

    [Test]
    public void QueuePathForOpenFilesCheck_should_immediately_process_file_with_no_handles()
    {
      // Arrange
      var parameters = new OperatingParameters();

      parameters.MaximumTimeToWaitForNoOpenFileHandles = TimeSpan.FromSeconds(0.1);
      parameters.OpenFileHandlePollingInterval = TimeSpan.Zero;

      var sut = CreateSUT(
        parameters,
        out var timer,
        out var checksum,
        out var monitor,
        out var openFileHandles,
        out var zfs,
        out var staging,
        out var remoteFileStateCache,
        out var storage);

      openFileHandles.Enumerate(Arg.Any<string>()).Returns(new OpenFileHandle[0]);

      string filePath = _faker.System.FilePath();

      var snapshot = Substitute.For<IZFSSnapshot>();

      var referenceTracker = new SnapshotReferenceTracker(snapshot);

      var reference = new SnapshotReference(referenceTracker, filePath);

      try
      {
        sut.StartPollOpenFilesThread();

        // Act
        sut.QueuePathForOpenFilesCheck(reference);
        bool backupQueueInitiallyEmpty = !sut.BackupQueue.Any();
        Thread.Sleep(parameters.MaximumTimeToWaitForNoOpenFileHandles * 2);

        // Assert
        backupQueueInitiallyEmpty.Should().BeTrue();
        sut.BackupQueue.Should().Contain(reference);
      }
      finally
      {
        sut.Stop();
      }
    }

    [Test]
    public void QueuePathForOpenFilesCheck_should_wait_until_file_has_no_handles()
    {
      // Arrange
      var autoFaker = AutoFaker.Create();

      var parameters = new OperatingParameters();

      parameters.MaximumTimeToWaitForNoOpenFileHandles = TimeSpan.FromSeconds(1);
      parameters.OpenFileHandlePollingInterval = TimeSpan.FromSeconds(0.1);

      var sut = CreateSUT(
        parameters,
        out var timer,
        out var checksum,
        out var monitor,
        out var openFileHandles,
        out var zfs,
        out var staging,
        out var remoteFileStateCache,
        out var storage);

      bool hasOpenFileHandles = true;

      openFileHandles.Enumerate(Arg.Any<string>()).Returns(
        x =>
        {
          var path = x.Arg<string>();

          if (hasOpenFileHandles)
            return new[] { autoFaker.Generate<OpenFileHandle>() };
          else
            return new OpenFileHandle[0];
        });

      string filePath = _faker.System.FilePath();

      var snapshot = Substitute.For<IZFSSnapshot>();

      string snapshotPath = _faker.System.DirectoryPath();

      snapshot.BuildPath().Returns(snapshotPath);

      string fileInSnapshotPath = Path.Combine(snapshotPath, filePath.TrimStart('/'));

      var referenceTracker = new SnapshotReferenceTracker(snapshot);

      var reference = new SnapshotReference(referenceTracker, filePath);

      sut.StartPollOpenFilesThread();

      try
      {
        // Act & Assert
        sut.QueuePathForOpenFilesCheck(reference);

        sut.BackupQueue.Should().BeEmpty();

        Thread.Sleep(parameters.OpenFileHandlePollingInterval * 1.5);
    
        sut.BackupQueue.Should().BeEmpty();
        openFileHandles.Received().Enumerate(filePath);
        openFileHandles.ClearReceivedCalls();

        hasOpenFileHandles = false;

        Thread.Sleep(parameters.OpenFileHandlePollingInterval);

        openFileHandles.Received().Enumerate(filePath);
        sut.BackupQueue.Should().Contain(reference);
      }
      finally
      {
        sut.Stop();
      }
    }

    [Test]
    public void ProcessBackupQueueReference_should_not_queue_unchanged_file()
    {
      // Arrange
      using (var file = new TemporaryFile())
      {
        var parameters = new OperatingParameters();

        parameters.MaximumTimeToWaitForNoOpenFileHandles = TimeSpan.FromSeconds(0.1);
        parameters.OpenFileHandlePollingInterval = TimeSpan.Zero;
        parameters.MaximumFileSizeForStagingCopy = 100;

        var snapshot = Substitute.For<IZFSSnapshot>();

        snapshot.BuildPath().Returns("/");

        var snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot);

        var reference = new SnapshotReference(snapshotReferenceTracker, file.Path);

        var sut = CreateSUT(
          parameters,
          out var timer,
          out var checksum,
          out var monitor,
          out var openFileHandles,
          out var zfs,
          out var staging,
          out var remoteFileStateCache,
          out var storage);

        checksum.ComputeChecksum(Arg.Any<string>()).Returns(
          x =>
          {
            using (var stream = File.OpenRead(x.Arg<string>()))
              return checksum.ComputeChecksum(stream);
          });

        var cachedFileState = new FileState();

        cachedFileState.Path = file.Path;
        cachedFileState.FileSize = parameters.MaximumFileSizeForStagingCopy;

        File.WriteAllBytes(file.Path, _faker.Random.Bytes((int)cachedFileState.FileSize));

        cachedFileState.LastModifiedUTC = File.GetLastWriteTimeUtc(file.Path);
        cachedFileState.Checksum = checksum.ComputeChecksum(file.Path);

        remoteFileStateCache.GetFileState(file.Path).Returns(cachedFileState);

        // Act
        sut.ProcessBackupQueueReference(reference);

        // Assert
        remoteFileStateCache.Received().GetFileState(file.Path);
        staging.DidNotReceive().StageFile(Arg.Any<Stream>());
        sut.UploadQueue.Should().BeEmpty();
      }
    }

    [Test]
    public void ProcessBackupQueueReference_should_queue_file_in_place_for_large_files()
    {
      // Arrange
      using (var file = new TemporaryFile())
      {
        var parameters = new OperatingParameters();

        parameters.MaximumTimeToWaitForNoOpenFileHandles = TimeSpan.FromSeconds(0.1);
        parameters.OpenFileHandlePollingInterval = TimeSpan.Zero;
        parameters.MaximumFileSizeForStagingCopy = 100;

        var snapshot = Substitute.For<IZFSSnapshot>();

        snapshot.BuildPath().Returns("/");

        var snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot);

        var reference = new SnapshotReference(snapshotReferenceTracker, file.Path);

        var sut = CreateSUT(
          parameters,
          out var timer,
          out var checksum,
          out var monitor,
          out var openFileHandles,
          out var zfs,
          out var staging,
          out var remoteFileStateCache,
          out var storage);

        checksum.ComputeChecksum(Arg.Any<string>()).Returns(
          x =>
          {
            using (var stream = File.OpenRead(x.Arg<string>()))
              return checksum.ComputeChecksum(stream);
          });

        File.WriteAllBytes(file.Path, _faker.Random.Bytes((int)parameters.MaximumFileSizeForStagingCopy + 1));

        // Act
        sut.ProcessBackupQueueReference(reference);

        // Assert
        remoteFileStateCache.Received().GetFileState(file.Path);
        staging.DidNotReceive().StageFile(Arg.Any<Stream>());

        var fileReference = sut.UploadQueue.Single();

        fileReference.Path.Should().Be(file.Path);
      }
    }

    [Test]
    public void ProcessBackupQueueReference_should_stage_file_for_small_files()
    {
      // Arrange
      using (var file = new TemporaryFile())
      {
        var parameters = new OperatingParameters();

        parameters.MaximumTimeToWaitForNoOpenFileHandles = TimeSpan.FromSeconds(0.1);
        parameters.OpenFileHandlePollingInterval = TimeSpan.Zero;
        parameters.MaximumFileSizeForStagingCopy = 100;

        var snapshot = Substitute.For<IZFSSnapshot>();

        snapshot.BuildPath().Returns("/");

        var snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot);

        var reference = new SnapshotReference(snapshotReferenceTracker, file.Path);

        var sut = CreateSUT(
          parameters,
          out var timer,
          out var checksum,
          out var monitor,
          out var openFileHandles,
          out var zfs,
          out var staging,
          out var remoteFileStateCache,
          out var storage);

        checksum.ComputeChecksum(Arg.Any<string>()).Returns(
          x =>
          {
            using (var stream = File.OpenRead(x.Arg<string>()))
              return checksum.ComputeChecksum(stream);
          });

        File.WriteAllBytes(file.Path, _faker.Random.Bytes((int)parameters.MaximumFileSizeForStagingCopy));

        var stagedFile = Substitute.For<IStagedFile>();

        var stagedFilePath = "/tmp/" + _faker.System.FileName();

        stagedFile.Path.Returns(stagedFilePath);

        staging.StageFile(Arg.Any<Stream>()).Returns(
          x =>
          {
            File.Copy(file.Path, stagedFilePath);

            return stagedFile;
          });

        // Act
        sut.ProcessBackupQueueReference(reference);

        // Assert
        remoteFileStateCache.Received().GetFileState(file.Path);
        staging.Received().StageFile(Arg.Any<Stream>());

        var fileReference = sut.UploadQueue.Single();

        fileReference.Path.Should().Be(stagedFilePath);
      }
    }

    [Test]
    public void UploadThreadProc_should_upload_files()
    {
      // Arrange
      var autoFaker = AutoFaker.Create();

      var parameters = new OperatingParameters();

      var sut = CreateSUT(
        parameters,
        out var timer,
        out var checksum,
        out var monitor,
        out var openFileHandles,
        out var zfs,
        out var staging,
        out var remoteFileStateCache,
        out var storage);

      sut.Start();

      var fileReferences =
        new[]
        {
          autoFaker.Generate<FileReference>(),
          autoFaker.Generate<FileReference>(),
          autoFaker.Generate<FileReference>(),
        };

      foreach (var reference in fileReferences)
        reference.Stream = new MemoryStream();

      try
      {
        // Act
        foreach (var reference in fileReferences)
          sut.AddFileReferenceToUploadQueue(reference);

        for (int i=0; i < 10; i++)
        {
          if (sut.UploadQueue.Count() == 0)
            break;

          Thread.Sleep(50);
        }

        // Assert
        sut.UploadQueue.Should().BeEmpty();

        foreach (var reference in fileReferences)
          storage.Received().UploadFile(Arg.Any<string>(), Arg.Is(reference.Stream));
      }
      finally
      {
        sut.Stop();
      }
    }
  }
}

