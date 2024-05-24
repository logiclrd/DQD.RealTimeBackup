using System;
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

using DeltaQ.RTB.ActivityMonitor;
using DeltaQ.RTB.Agent;
using DeltaQ.RTB.FileSystem;
using DeltaQ.RTB.Interop;
using DeltaQ.RTB.StateCache;
using DeltaQ.RTB.Storage;
using DeltaQ.RTB.SurfaceArea;
using DeltaQ.RTB.Utility;

using ITimer = DeltaQ.RTB.Utility.ITimer;

namespace DeltaQ.RTB.Tests.Fixtures.Agent
{
	[TestFixture]
	public class BackupAgentTests
	{
		Faker _faker = new Faker();

		BackupAgent CreateSUT(OperatingParameters parameters, out ITimer timer, out IChecksum checksum, out ISurfaceArea surfaceArea, out IFileSystemMonitor monitor, out IOpenFileHandles openFileHandles, out IZFS zfs, out IStaging staging, out IRemoteFileStateCache remoteFileStateCache, out IRemoteStorage storage)
		{
			timer = Substitute.For<ITimer>();
			checksum = Substitute.For<IChecksum>();
			surfaceArea = Substitute.For<ISurfaceArea>();
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
				surfaceArea,
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
				out var surfaceArea,
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
				sut.BackupQueue.Should().Contain(x => (x is UploadAction) && (((UploadAction)x).Source == reference));
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
				out var surfaceArea,
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
					{
						var openFileHandle = autoFaker.Generate<OpenFileHandle>();

						openFileHandle.FileAccess = FileAccess.Write;

						return new[] { openFileHandle };
					}
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
				sut.BackupQueue.Should().Contain(x => (x is UploadAction) && (((UploadAction)x).Source == reference));
			}
			finally
			{
				sut.Stop();
			}
		}

		[Test]
		public void ProcessBackupQueueAction_should_not_queue_unchanged_file()
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

				var uploadAction = new UploadAction(reference, reference.Path);

				var sut = CreateSUT(
					parameters,
					out var timer,
					out var checksum,
					out var surfaceArea,
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
				sut.ProcessBackupQueueAction(uploadAction);

				// Assert
				remoteFileStateCache.Received().GetFileState(file.Path);
				staging.DidNotReceive().StageFile(Arg.Any<Stream>());
				sut.PeekUploadQueue().Should().BeEmpty();
			}
		}

		[Test]
		public void ProcessBackupQueueAction_should_queue_file_in_place_for_large_files()
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

				var uploadAction = new UploadAction(reference, reference.Path);

				var sut = CreateSUT(
					parameters,
					out var timer,
					out var checksum,
					out var surfaceArea,
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
				sut.ProcessBackupQueueAction(uploadAction);

				// Assert
				remoteFileStateCache.Received().GetFileState(file.Path);
				staging.DidNotReceive().StageFile(Arg.Any<Stream>());

				var fileReference = sut.PeekUploadQueue().Single();

				fileReference.Path.Should().Be(file.Path);
			}
		}

		[Test]
		public void ProcessBackupQueueAction_should_stage_file_for_small_files()
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

				var uploadAction = new UploadAction(reference, reference.Path);

				var sut = CreateSUT(
					parameters,
					out var timer,
					out var checksum,
					out var surfaceArea,
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
				sut.ProcessBackupQueueAction(uploadAction);

				// Assert
				remoteFileStateCache.Received().GetFileState(file.Path);
				staging.Received().StageFile(Arg.Any<Stream>());

				var fileReference = sut.PeekUploadQueue().Single();

				fileReference.Path.Should().Be(file.Path);
				fileReference.StagedFile.Should().NotBeNull();
				fileReference.StagedFile!.Path.Should().Be(stagedFilePath);
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
				out var surfaceArea,
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

				for (int i = 0; i < 10; i++)
				{
					if (sut.PeekUploadQueue().Count() == 0)
						break;

					Thread.Sleep(50);
				}

				// Assert
				sut.PeekUploadQueue().Should().BeEmpty();

				foreach (var reference in fileReferences)
					storage.Received().UploadFile(Arg.Any<string>(), Arg.Is(reference.Stream), Arg.Any<CancellationToken>());
			}
			finally
			{
				sut.Stop();
			}
		}
	}
}

