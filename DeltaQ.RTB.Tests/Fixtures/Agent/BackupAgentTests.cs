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

using DeltaQ.RTB.ActivityMonitor;
using DeltaQ.RTB.Agent;
using DeltaQ.RTB.Diagnostics;
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

		BackupAgent CreateSUT(OperatingParameters parameters, out IErrorLogger errorLogger, out ITimer timer, out IChecksum checksum, out ISurfaceArea surfaceArea, out IFileSystemMonitor monitor, out IOpenFileHandles openFileHandles, out IZFS zfs, out IStaging staging, out IRemoteFileStateCache remoteFileStateCache, out IRemoteStorage storage)
		{
			errorLogger = Substitute.For<IErrorLogger>();
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
				errorLogger,
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
				out var errorLogger,
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

			var referenceTracker = new SnapshotReferenceTracker(snapshot, errorLogger);

			var reference = new SnapshotReference(referenceTracker, filePath, errorLogger);

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
				out var errorLogger,
				out var timer,
				out var checksum,
				out var surfaceArea,
				out var monitor,
				out var openFileHandles,
				out var zfs,
				out var staging,
				out var remoteFileStateCache,
				out var storage);

			string filePath = _faker.System.FilePath();

			bool hasOpenFileHandles = true;

			openFileHandles.EnumerateAll().Returns(
				x =>
				{
					if (hasOpenFileHandles)
					{
						var openFileHandle = autoFaker.Generate<OpenFileHandle>();

						openFileHandle.FileAccess = FileAccess.Write;
						openFileHandle.FileName = filePath;

						return new[] { openFileHandle };
					}
					else
						return new OpenFileHandle[0];
				});

			var snapshot = Substitute.For<IZFSSnapshot>();

			string snapshotPath = _faker.System.DirectoryPath();

			snapshot.BuildPath().Returns(snapshotPath);

			string fileInSnapshotPath = Path.Combine(snapshotPath, filePath.TrimStart('/'));

			var referenceTracker = new SnapshotReferenceTracker(snapshot, errorLogger);

			var reference = new SnapshotReference(referenceTracker, filePath, errorLogger);

			sut.StartPollOpenFilesThread();

			try
			{
				// Act & Assert
				sut.QueuePathForOpenFilesCheck(reference);

				sut.BackupQueue.Should().BeEmpty();

				Thread.Sleep(parameters.OpenFileHandlePollingInterval * 1.5);

				sut.BackupQueue.Should().BeEmpty();
				openFileHandles.Received().EnumerateAll();
				openFileHandles.ClearReceivedCalls();

				hasOpenFileHandles = false;

				Thread.Sleep(parameters.OpenFileHandlePollingInterval);

				openFileHandles.Received().EnumerateAll();
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

				var sut = CreateSUT(
					parameters,
					out var errorLogger,
					out var timer,
					out var checksum,
					out var surfaceArea,
					out var monitor,
					out var openFileHandles,
					out var zfs,
					out var staging,
					out var remoteFileStateCache,
					out var storage);

				var snapshot = Substitute.For<IZFSSnapshot>();

				snapshot.BuildPath().Returns("/");

				var snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot, errorLogger);

				var reference = new SnapshotReference(snapshotReferenceTracker, file.Path, errorLogger);

				var uploadAction = new UploadAction(reference, reference.Path);

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

				var sut = CreateSUT(
					parameters,
					out var errorLogger,
					out var timer,
					out var checksum,
					out var surfaceArea,
					out var monitor,
					out var openFileHandles,
					out var zfs,
					out var staging,
					out var remoteFileStateCache,
					out var storage);

				var snapshot = Substitute.For<IZFSSnapshot>();

				snapshot.BuildPath().Returns("/");

				var snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot, errorLogger);

				var reference = new SnapshotReference(snapshotReferenceTracker, file.Path, errorLogger);

				var uploadAction = new UploadAction(reference, reference.Path);

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
				errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

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

				var sut = CreateSUT(
					parameters,
					out var errorLogger,
					out var timer,
					out var checksum,
					out var surfaceArea,
					out var monitor,
					out var openFileHandles,
					out var zfs,
					out var staging,
					out var remoteFileStateCache,
					out var storage);

				var snapshot = Substitute.For<IZFSSnapshot>();

				snapshot.BuildPath().Returns("/");

				var snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot, errorLogger);

				var reference = new SnapshotReference(snapshotReferenceTracker, file.Path, errorLogger);

				var uploadAction = new UploadAction(reference, reference.Path);

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
				errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

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
			var parameters = new OperatingParameters();

			var sut = CreateSUT(
				parameters,
				out var errorLogger,
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

			var fileReferences = new List<FileReference>();
			var temporaryFiles = new List<TemporaryFile>();

			void CreateFakeFileReference()
			{
				var temporaryFile = new TemporaryFile();

				temporaryFiles.Add(temporaryFile);

				File.WriteAllText(temporaryFile.Path, _faker.Lorem.Paragraph());

				string path = _faker.System.FilePath();;
				var stagedFile = Substitute.For<IStagedFile>();
				DateTime lastModifiedUTC = _faker.Date.Recent();
				var checksum = _faker.Random.Hash(length: 32);

				stagedFile.Path.Returns(temporaryFile.Path);

				var fileReference = new FileReference(path, stagedFile, lastModifiedUTC, checksum);

				fileReferences.Add(fileReference);
			}

			CreateFakeFileReference();
			CreateFakeFileReference();
			CreateFakeFileReference();

			try
			{
				var fileContents = new List<string>();

				var uploadedFiles = new List<(string Path, string Content)>();

				foreach (var reference in fileReferences)
				{
					var temporaryFile = new TemporaryFile();

					reference.SourcePath = temporaryFile.Path;

					string contents = Guid.NewGuid().ToString();

					File.WriteAllText(reference.SourcePath, contents);

					fileContents.Add(contents);
				}

				storage.When(x => x.UploadFile(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<Action<UploadProgress>>(), Arg.Any<CancellationToken>())).Do(
					x =>
					{
						string path = x.Arg<string>();
						Stream stream = x.Arg<Stream>();

						string contents = new StreamReader(stream).ReadToEnd();

						lock (uploadedFiles)
							uploadedFiles.Add((path, contents));
					});

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

				uploadedFiles.Should().HaveSameCount(fileReferences);

				for (int i=0; i < fileReferences.Count; i++)
				{
					var uploadedFile = uploadedFiles.Single(file => file.Path == BackupAgent.PlaceInContentPath(fileReferences[i].Path));

					uploadedFiles.Remove(uploadedFile);

					uploadedFile.Content.Should().Be(fileContents[i]);
				}
			}
			finally
			{
				sut.Stop();

				temporaryFiles.ForEach(file => file.Dispose());
			}
		}
	}
}

