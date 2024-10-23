using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using NSubstitute;

using Bogus;

using AutoBogus;

using FluentAssertions;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.Utility;

using DQD.RealTimeBackup.Tests.Support;

using CancellationToken = System.Threading.CancellationToken;

namespace DQD.RealTimeBackup.Tests.Fixtures.StateCache
{
	[TestFixture]
	public class RemoteFileStateCacheTests
	{
		class DummyStorage : IRemoteFileStateCacheStorage
		{
#pragma warning disable 67
			public event EventHandler<DiagnosticMessage>? DiagnosticOutput;
#pragma warning restore 67

			public Dictionary<int, MemoryStream> BatchData = new Dictionary<int, MemoryStream>();
			public Dictionary<int, MemoryStream> NewBatchData = new Dictionary<int, MemoryStream>();

			public void InitializeWithBatches(params IEnumerable<FileState>[] batches)
				=> InitializeWithBatches(batches.AsEnumerable());

			public void InitializeWithBatches(IEnumerable<IEnumerable<FileState>> batches)
			{
				foreach (var batch in batches)
				{
					int batchNumber = BatchData.Count + 1;

					using (var writer = OpenBatchFileWriter(batchNumber))
						foreach (var fileState in batch)
							writer.WriteLine(fileState);
				}
			}

			public IEnumerable<BatchFileInfo> EnumerateBatches()
			{
				foreach (var (batchNumber, batchFileData) in BatchData)
				{
					yield return
						new BatchFileInfo()
						{
							BatchNumber = batchNumber,
							Path = "/batch/" + batchNumber,
							FileSize = batchFileData.Length,
						};
				}
			}

			public int GetBatchFileSize(int batchNumber)
			{
				if (BatchData.TryGetValue(batchNumber, out var batchStream))
					return (int)batchStream.Length;
				else
					return -1;
			}

			public StreamWriter OpenBatchFileWriter(int batchNumber)
			{
				var stream = BatchData[batchNumber] = new MemoryStream();

				return new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
			}

			public StreamReader OpenBatchFileReader(int batchNumber)
			{
				return new StreamReader(OpenBatchFileStream(batchNumber), leaveOpen: true);
			}

			public Stream OpenBatchFileStream(int batchNumber)
			{
				var stream = BatchData[batchNumber];

				return new MemoryStream(stream.ToArray());
			}

			public StreamWriter OpenNewBatchFileWriter(int batchNumber)
			{
				if (!NewBatchData.TryGetValue(batchNumber, out var stream))
					NewBatchData[batchNumber] = stream = new MemoryStream();

				return new StreamWriter(stream, leaveOpen: true);
			}

			public void SwitchToConsolidatedFile(IEnumerable<int> mergedBatchNumbersForDeletion, int mergeIntoBatchNumber)
			{
				BatchData[mergeIntoBatchNumber] = NewBatchData[mergeIntoBatchNumber];
				NewBatchData.Remove(mergeIntoBatchNumber);
				foreach (var mergedBatchNumber in mergedBatchNumbersForDeletion)
					BatchData.Remove(mergedBatchNumber);
			}
		}

		IAutoFaker CreateAutoFaker(Faker? fakerHub = null)
		{
			return AutoFaker.Create(
				config =>
				{
					config.WithOverride(
						(FileState fs) => fs.Checksum,
						ctx => ctx.Faker.Random.Hash(length: 32));

					if (fakerHub != null)
						config.WithFakerHub(fakerHub);
				});
		}

		[Test]
		public void LoadCache_should_load_cache_from_local_storage()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var dummyStorage = new DummyStorage();

			var batch1FileStates = new List<FileState>();
			var batch2FileStates = new List<FileState>();
			var batch3FileStates = new List<FileState>();

			for (int i=0; i < 10; i++)
				batch1FileStates.Add(faker.Generate<FileState>());
			for (int i=0; i < 20; i++)
				batch2FileStates.Add(faker.Generate<FileState>());
			for (int i=0; i < 5; i++)
				batch3FileStates.Add(faker.Generate<FileState>());

			var expectedCache = new Dictionary<string, FileState>();

			foreach (var fileState in batch1FileStates.Concat(batch2FileStates).Concat(batch3FileStates))
				expectedCache[fileState.Path] = fileState;

			dummyStorage.InitializeWithBatches(
				batch1FileStates,
				batch2FileStates,
				batch3FileStates);

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			var sut = new RemoteFileStateCache(
				parameters,
				errorLogger,
				timer,
				dummyStorage,
				cacheActionLog,
				remoteStorage);

			// Act
			sut.LoadCache();

			// Assert
			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

			sut.GetCacheForTest().Should().BeEquivalentTo(expectedCache);
		}

		[Test]
		public void GetFileState_should_return_states_from_cache()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var dummyStorage = new DummyStorage();

			var fileStates = new List<FileState>();

			for (int i=0; i < 10; i++)
			{
				var state = faker.Generate<FileState>();

				state.IsInParts = false;

				state = FileState.Parse(state.ToString());

				fileStates.Add(state);
			}

			dummyStorage.InitializeWithBatches(fileStates);

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			// Act
			var result = new RemoteFileStateCache(
				parameters,
				errorLogger,
				timer,
				dummyStorage,
				cacheActionLog,
				remoteStorage);

			// Assert
			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

			foreach (var fileState in fileStates)
				result.GetFileState(fileState.Path).Should().BeEquivalentTo(fileState);
		}

		[Test]
		public void GetFileState_should_return_null_for_uncached_path()
		{
			// Arrange
			var faker = new Faker();

			var dummyStorage = new DummyStorage();

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			// Act
			var result = new RemoteFileStateCache(
				parameters,
				errorLogger,
				timer,
				dummyStorage,
				cacheActionLog,
				remoteStorage);

			// Assert
			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

			result.GetFileState(faker.System.FilePath()).Should().BeNull();
		}

		[Test]
		public void UpdateFileState_should_update_cache()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var fileState = faker.Generate<FileState>();
			var newFileState = faker.Generate<FileState>();

			var dummyStorage = new DummyStorage();

			dummyStorage.InitializeWithBatches(new[] { fileState });

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			var sut = new RemoteFileStateCache(
				parameters,
				errorLogger,
				timer,
				dummyStorage,
				cacheActionLog,
				remoteStorage);

			// Act
			var before = sut.GetFileState(fileState.Path);

			sut.UpdateFileState(fileState.Path, newFileState);

			var after = sut.GetFileState(fileState.Path);

			// Assert
			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

			before.Should().BeEquivalentTo(fileState);
			after.Should().BeEquivalentTo(newFileState);
		}

		[Test]
		public void UpdateFileState_should_append_to_batch()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var fileState = faker.Generate<FileState>();
			var newFileState = faker.Generate<FileState>();

			var dummyStorage = new DummyStorage();

			dummyStorage.InitializeWithBatches(new[] { fileState });

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			var sut = new RemoteFileStateCache(
				parameters,
				errorLogger,
				timer,
				dummyStorage,
				cacheActionLog,
				remoteStorage);

			// Act
			var maxBatchNumberBefore = dummyStorage.EnumerateBatches().Select(batch => batch.BatchNumber).Max();

			sut.UpdateFileState(fileState.Path, newFileState);

			var maxBatchNumberAfter = dummyStorage.EnumerateBatches().Select(batch => batch.BatchNumber).Max();

			// Assert
			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

			maxBatchNumberAfter.Should().Be(maxBatchNumberBefore + 1);

			using (var reader = dummyStorage.OpenBatchFileReader(maxBatchNumberAfter))
			{
				var persistedFileState = FileState.Parse(reader.ReadLine()!);

				persistedFileState.Should().BeEquivalentTo(newFileState);
			}
		}

		[Test]
		public void RemoveFileState_should_update_cache()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var fileState = faker.Generate<FileState>();

			fileState.IsInParts = false;

			fileState = FileState.Parse(fileState.ToString());

			var dummyStorage = new DummyStorage();

			dummyStorage.InitializeWithBatches(new[] { fileState });

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			var sut = new RemoteFileStateCache(
				parameters,
				errorLogger,
				timer,
				dummyStorage,
				cacheActionLog,
				remoteStorage);

			// Act
			var before = sut.GetFileState(fileState.Path);

			sut.RemoveFileState(fileState.Path);

			var after = sut.GetFileState(fileState.Path);

			// Assert
			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

			before.Should().BeEquivalentTo(fileState);
			after.Should().BeNull();
		}

		[Test]
		public void RemoveFileState_should_append_to_batch()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var fileState = faker.Generate<FileState>();

			var dummyStorage = new DummyStorage();

			dummyStorage.InitializeWithBatches(new[] { fileState });

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			var sut = new RemoteFileStateCache(
				parameters,
				errorLogger,
				timer,
				dummyStorage,
				cacheActionLog,
				remoteStorage);

			// Act
			var maxBatchNumberBefore = dummyStorage.EnumerateBatches().Select(batch => batch.BatchNumber).Max();

			sut.RemoveFileState(fileState.Path);

			var maxBatchNumberAfter = dummyStorage.EnumerateBatches().Select(batch => batch.BatchNumber).Max();

			// Assert
			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

			maxBatchNumberAfter.Should().Be(maxBatchNumberBefore + 1);

			using (var reader = dummyStorage.OpenBatchFileReader(maxBatchNumberAfter))
			{
				var persistedFileState = FileState.Parse(reader.ReadLine()!);

				persistedFileState.Path.Should().Be(fileState.Path);
				persistedFileState.FileSize.Should().Be(RemoteFileStateCache.DeletedFileSize);
				persistedFileState.Checksum.Should().Be(RemoteFileStateCache.DeletedChecksum);
			}
		}

		public void UploadCurrentBatchAndBeginNext_should_begin_next_batch()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var dummyStorage = new DummyStorage();

			var fileStates = new List<FileState>();
			var newFileStates = new List<FileState>();

			for (int i=0; i < 10; i++)
				fileStates.Add(faker.Generate<FileState>());
			for (int i=0; i < 15; i++)
				newFileStates.Add(faker.Generate<FileState>());

			dummyStorage.InitializeWithBatches(fileStates);

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			var uploads = new List<(string ServerPath, byte[] Content)>();

			remoteStorage.When(x => x.UploadFileDirect(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())).Do(
				x =>
				{
					var serverPath = x.Arg<string>();
					var content = x.Arg<Stream>();

					byte[] contentBytes = new byte[content.Length];

					content.Read(contentBytes, 0, contentBytes.Length);

					uploads.Add((serverPath, contentBytes));
				});

			var sut = new RemoteFileStateCache(
				parameters,
				errorLogger,
				timer,
				dummyStorage,
				cacheActionLog,
				remoteStorage);

			foreach (var fileState in newFileStates)
				sut.UpdateFileState(fileState.Path, fileState);

			int batchNumber = sut.GetCurrentBatchNumberForTest();

			// Act
			sut.UploadCurrentBatchAndBeginNext();

			// Assert
			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

			sut.GetCurrentBatchNumberForTest().Should().Be(batchNumber + 1);
			sut.GetCurrentBatchForTest().Should().BeEmpty();
		}

		[Test]
		public void UploadCurrentBatchAndBeginNext_should_send_batch_to_remote_storage()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var dummyStorage = new DummyStorage();

			var fileStates = new List<FileState>();
			var newFileStates = new List<FileState>();

			for (int i=0; i < 10; i++)
				fileStates.Add(FileState.Parse(faker.Generate<FileState>().ToString()));
			for (int i=0; i < 15; i++)
				newFileStates.Add(FileState.Parse(faker.Generate<FileState>().ToString()));

			dummyStorage.InitializeWithBatches(fileStates);

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			using (var temporaryFile = new TemporaryFile())
			{
				cacheActionLog.CreateTemporaryCacheActionDataFile().Returns(_ => temporaryFile.Path);

				var uploads = new List<(string ServerPath, byte[] Content)>();

				remoteStorage.When(x => x.UploadFileDirect(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())).Do(
					x =>
					{
						var serverPath = x.Arg<string>();
						var content = x.Arg<Stream>();

						byte[] contentBytes = new byte[content.Length];

						content.Read(contentBytes, 0, contentBytes.Length);

						uploads.Add((serverPath, contentBytes));
					});

				var sut = new RemoteFileStateCache(
					parameters,
					errorLogger,
					timer,
					dummyStorage,
					cacheActionLog,
					remoteStorage);

				sut.Start();

				foreach (var fileState in newFileStates)
					sut.UpdateFileState(fileState.Path, fileState);

				int batchNumber = sut.GetCurrentBatchNumberForTest();

				// Act
				sut.UploadCurrentBatchAndBeginNext();

				// Assert
				errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

				sut.DrainActionQueue();

				cacheActionLog.Received(1).CreateTemporaryCacheActionDataFile();

				uploads.Should().HaveCount(1);

				var upload = uploads.Single();

				upload.ServerPath.Should().Be("/state/" + batchNumber);

				var stream = new MemoryStream(upload.Content);

				var uploadedFileStates = new List<FileState>();

				using (var reader = new StreamReader(stream))
				{
					while (true)
					{
						string? line = reader.ReadLine();

						if (line == null)
							break;

						uploadedFileStates.Add(FileState.Parse(line));
					}
				}

				uploadedFileStates.Should().BeEquivalentTo(newFileStates);
			}
		}

		[Test]
		public void UploadCurrentBatchAndBeginNext_should_consolidate_batches_when_threshold_is_hit()
		{
			// Arrange
			RemoteFileStateCache.ConsolidationBatchCountMinimum = 5;
			RemoteFileStateCache.ConsolidationBatchBytesPerAdditionalBatch = int.MaxValue;

			var faker = new Faker();

			var autoFaker = CreateAutoFaker(fakerHub: faker);

			var dummyStorage = new DummyStorage();

			var batches = new List<List<FileState>>();

			for (int i=0; i < RemoteFileStateCache.ConsolidationBatchCountMinimum + 1; i++)
			{
				var batchFileStates = new List<FileState>();

				for (int j=0, l=faker.Random.Number(10, 20); j < l; j++)
				{
					var state = autoFaker.Generate<FileState>();

					state = FileState.Parse(state.ToString());

					batchFileStates.Add(state);
				}

				batches.Add(batchFileStates);
			}

			// Ensure that, as a starting point, none of the paths match.
			var pathUsed = new HashSet<string>();

			foreach (var state in batches.SelectMany(batch => batch))
				while (!pathUsed.Add(state.Path))
					state.Path = faker.System.FilePath();

			// Now ensure that some of the paths match.
			for (int i = 1; i < batches.Count; i++)
			{
				var pathsToCopy = faker.PickRandom(batches[i - 1], amountToPick: 5).ToList();

				for (int j = 0; j < 5; j++)
					faker.PickRandom(batches[i]).Path = pathsToCopy[j].Path;
			}

			var newFileStates = new List<FileState>();

			for (int i=0; i < 15; i++)
			{
				var state = autoFaker.Generate<FileState>();

				state = FileState.Parse(state.ToString());

				newFileStates.Add(state);
			}

			dummyStorage.InitializeWithBatches(batches);

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			//parameters.RemoteFileStateCacheDebugLogPath = "/tmp/rfsc.test.debuglog";
			//
			//File.Delete(parameters.RemoteFileStateCacheDebugLogPath);

			using (var temporaryFile1 = new TemporaryFile())
			using (var temporaryFile2 = new TemporaryFile())
			{
				var temporaryFilePaths = new Queue<string>(new[] { temporaryFile1.Path, temporaryFile2.Path });

				cacheActionLog.CreateTemporaryCacheActionDataFile().Returns(_ => temporaryFilePaths.Dequeue());

				var uploads = new List<(string ServerPath, byte[] Content)>();
				var deletions = new List<string>();

				remoteStorage.When(x => x.UploadFileDirect(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())).Do(
					x =>
					{
						var serverPath = x.Arg<string>();
						var content = x.Arg<Stream>();

						byte[] contentBytes = new byte[content.Length];

						content.Read(contentBytes, 0, contentBytes.Length);

						uploads.Add((serverPath, contentBytes));
					});

				remoteStorage.When(x => x.DeleteFileDirect(Arg.Any<string>(), Arg.Any<CancellationToken>())).Do(
					x =>
					{
						var serverPath = x.Arg<string>();

						deletions.Add(serverPath);
					});

				var sut = new RemoteFileStateCache(
					parameters,
					errorLogger,
					timer,
					dummyStorage,
					cacheActionLog,
					remoteStorage);

				sut.Start();

				foreach (var fileState in newFileStates)
					sut.UpdateFileState(fileState.Path, fileState);

				int batchNumber = sut.GetCurrentBatchNumberForTest();

				// Act
				sut.UploadCurrentBatchAndBeginNext();

				// Assert
				errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

				cacheActionLog.Received(2).CreateTemporaryCacheActionDataFile();

				sut.DrainActionQueue();

				uploads.Should().HaveCount(2);
				deletions.Should().HaveCount(RemoteFileStateCache.ConsolidationBatchCountMinimum - 1);

				uploads.Should().Contain(upload => upload.ServerPath == "/state/" + batchNumber);
				uploads.Should().Contain(upload => upload.ServerPath == "/state/" + RemoteFileStateCache.ConsolidationBatchCountMinimum);

				for (int i = 1; i < RemoteFileStateCache.ConsolidationBatchCountMinimum; i++)
					deletions.Should().Contain("/state/" + i);
			}
		}

		[Test]
		public void ConsolidateBatches_should_merge_oldest_batches_into_next_oldest()
		{
			// Arrange
			RemoteFileStateCache.ConsolidationBatchCountMinimum = 5;
			RemoteFileStateCache.ConsolidationBatchBytesPerAdditionalBatch = int.MaxValue;

			var faker = new Faker();

			var autoFaker = CreateAutoFaker(faker);

			var dummyStorage = new DummyStorage();

			var batches = new List<List<FileState>>();

			for (int i=0; i < RemoteFileStateCache.ConsolidationBatchCountMinimum + 1; i++)
			{
				var batchFileStates = new List<FileState>();

				for (int j=0, l=faker.Random.Number(10, 20); j < l; j++)
				{
					var state = autoFaker.Generate<FileState>();

					state = FileState.Parse(state.ToString());

					batchFileStates.Add(state);
				}

				batches.Add(batchFileStates);
			}

			// Ensure that, as a starting point, none of the paths match.
			var pathUsed = new HashSet<string>();

			foreach (var state in batches.SelectMany(batch => batch))
				while (!pathUsed.Add(state.Path))
					state.Path = faker.System.FilePath();

			// Now ensure that some of the paths match.
			for (int i = 1; i < batches.Count; i++)
			{
				var pathsToCopy = faker.PickRandom(batches[i - 1], amountToPick: 5).ToList();

				for (int j = 0; j < 5; j++)
					faker.PickRandom(batches[i]).Path = pathsToCopy[j].Path;
			}

			var expectedMergedFileStates = new List<FileState>(batches.First());

			// Overlay batches in sequence, preserving the order.
			foreach (var batch in batches.Skip(1).Take(RemoteFileStateCache.ConsolidationBatchCountMinimum - 1))
			{
				foreach (var fileState in batch)
				{
					bool found = false;

					for (int i=0; i < expectedMergedFileStates.Count; i++)
					{
						if (expectedMergedFileStates[i].Path == fileState.Path)
						{
							if (fileState.FileSize == RemoteFileStateCache.DeletedFileSize)
							{
								if ((fileState.PartNumber == 0)
								 || ((expectedMergedFileStates[i].IsInParts == fileState.IsInParts)
								  && (expectedMergedFileStates[i].PartNumber == fileState.PartNumber)))
								{
									expectedMergedFileStates.RemoveAt(i);
									i--;
								}
							}
							else if ((expectedMergedFileStates[i].IsInParts == fileState.IsInParts)
							      && (expectedMergedFileStates[i].PartNumber == fileState.PartNumber))
							{
								expectedMergedFileStates[i] = fileState;
								found = true;
								break;
							}
						}
					}

					if (!found && (fileState.FileSize != RemoteFileStateCache.DeletedFileSize))
						expectedMergedFileStates.Add(fileState);
				}
			}

			dummyStorage.InitializeWithBatches(batches);

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			using (var temporaryFile = new TemporaryFile())
			{
				cacheActionLog.CreateTemporaryCacheActionDataFile().Returns(temporaryFile.Path);

				var uploads = new List<(string ServerPath, byte[] Content)>();
				var deletions = new List<string>();

				remoteStorage.When(x => x.UploadFileDirect(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())).Do(
					x =>
					{
						var serverPath = x.Arg<string>();
						var content = x.Arg<Stream>();

						byte[] contentBytes = new byte[content.Length];

						content.Read(contentBytes, 0, contentBytes.Length);

						uploads.Add((serverPath, contentBytes));
					});

				remoteStorage.When(x => x.DeleteFileDirect(Arg.Any<string>(), Arg.Any<CancellationToken>())).Do(
					x =>
					{
						var serverPath = x.Arg<string>();

						deletions.Add(serverPath);
					});

				var sut = new RemoteFileStateCache(
					parameters,
					errorLogger,
					timer,
					dummyStorage,
					cacheActionLog,
					remoteStorage);

				sut.Start();

				// Act
				sut.ConsolidateBatches();

				// Assert
				errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

				int mergedBatchNumber = RemoteFileStateCache.ConsolidationBatchCountMinimum;

				cacheActionLog.Received(1).CreateTemporaryCacheActionDataFile();

				sut.DrainActionQueue();

				uploads.Should().HaveCount(1);

				var upload = uploads.Single();

				upload.ServerPath.Should().Be("/state/" + mergedBatchNumber);

				var stream = new MemoryStream(upload.Content);

				var uploadedFileStates = new List<FileState>();

				using (var reader = new StreamReader(stream))
				{
					while (true)
					{
						string? line = reader.ReadLine();

						if (line == null)
							break;

						uploadedFileStates.Add(FileState.Parse(line));
					}
				}

				uploadedFileStates.Should().BeEquivalentTo(expectedMergedFileStates);

				deletions.Should().HaveCount(RemoteFileStateCache.ConsolidationBatchCountMinimum - 1);

				for (int i = 1; i < RemoteFileStateCache.ConsolidationBatchCountMinimum; i++)
					deletions.Should().Contain("/state/" + i);
			}
		}

		[Test]
		public void ConsolidateBatches_should_omit_deleted_paths()
		{
			// Arrange
			RemoteFileStateCache.ConsolidationBatchCountMinimum = 5;
			RemoteFileStateCache.ConsolidationBatchBytesPerAdditionalBatch = int.MaxValue;

			var faker = new Faker();

			var autoFaker = CreateAutoFaker(faker);

			var dummyStorage = new DummyStorage();

			var batches = new List<List<FileState>>();

			for (int i=0; i < RemoteFileStateCache.ConsolidationBatchCountMinimum + 1; i++)
			{
				var batchFileStates = new List<FileState>();

				for (int j=0, l=faker.Random.Number(10, 20); j < l; j++)
				{
					var state = autoFaker.Generate<FileState>();

					state = FileState.Parse(state.ToString());

					batchFileStates.Add(state);
				}

				batches.Add(batchFileStates);
			}

			// Ensure that, as a starting point, none of the paths match.
			var pathUsed = new HashSet<string>();

			foreach (var state in batches.SelectMany(batch => batch))
				while (!pathUsed.Add(state.Path))
					state.Path = faker.System.FilePath();

			// Select paths to delete.
			var entriesToDelete = batches
				.Skip(1)
				.Take(RemoteFileStateCache.ConsolidationBatchCountMinimum - 1)
				.SelectMany(batch => faker.PickRandom(batch, 5))
				.ToList();

			foreach (var entry in entriesToDelete)
			{
				entry.FileSize = RemoteFileStateCache.DeletedFileSize;
				entry.Checksum = RemoteFileStateCache.DeletedChecksum;
				entry.IsInParts = false;
				entry.PartNumber = 0;
			}

			// Ensure that some of the deleted files get recreated in future batches.
			foreach (var entry in faker.PickRandom(entriesToDelete, entriesToDelete.Count / 5))
			{
				var indexOfBatchContainingDeletion = batches.FindIndex(batch => batch.Contains(entry));

				var candidates = batches.Skip(indexOfBatchContainingDeletion + 1).SelectMany(item => item).Except(entriesToDelete);

				var selected = faker.PickRandom(candidates);

				selected.Path = entry.Path;
				selected.IsInParts = false;
				selected.PartNumber = 0;
			}

			var expectedMergedFileStates = new List<FileState>();

			// Overlay batches in sequence, preserving the order.
			foreach (var batch in batches.Take(RemoteFileStateCache.ConsolidationBatchCountMinimum))
			{
				foreach (var fileState in batch)
				{
					bool found = false;

					for (int i=0; i < expectedMergedFileStates.Count; i++)
					{
						if (expectedMergedFileStates[i].Path == fileState.Path)
						{
							if (fileState.FileSize == RemoteFileStateCache.DeletedFileSize)
								expectedMergedFileStates.RemoveAt(i);
							else
								expectedMergedFileStates[i] = fileState;

							found = true;
							break;
						}
					}

					if (!found && (fileState.FileSize != RemoteFileStateCache.DeletedFileSize))
						expectedMergedFileStates.Add(fileState);
				}
			}

			dummyStorage.InitializeWithBatches(batches);

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			using (var temporaryFile = new TemporaryFile())
			{
				cacheActionLog.CreateTemporaryCacheActionDataFile().Returns(temporaryFile.Path);

				var uploads = new List<(string ServerPath, byte[] Content)>();
				var deletions = new List<string>();

				remoteStorage.When(x => x.UploadFileDirect(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())).Do(
					x =>
					{
						var serverPath = x.Arg<string>();
						var content = x.Arg<Stream>();

						byte[] contentBytes = new byte[content.Length];

						content.Read(contentBytes, 0, contentBytes.Length);

						uploads.Add((serverPath, contentBytes));
					});

				remoteStorage.When(x => x.DeleteFileDirect(Arg.Any<string>(), Arg.Any<CancellationToken>())).Do(
					x =>
					{
						var serverPath = x.Arg<string>();

						deletions.Add(serverPath);
					});

				var sut = new RemoteFileStateCache(
					parameters,
					errorLogger,
					timer,
					dummyStorage,
					cacheActionLog,
					remoteStorage);

				sut.Start();

				// Act
				sut.ConsolidateBatches();

				// Assert
				errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

				sut.DrainActionQueue();

				int mergedBatchNumber = RemoteFileStateCache.ConsolidationBatchCountMinimum;

				uploads.Should().HaveCount(1);

				var upload = uploads.Single();

				upload.ServerPath.Should().Be("/state/" + mergedBatchNumber);

				var stream = new MemoryStream(upload.Content);

				var uploadedFileStates = new List<FileState>();

				using (var reader = new StreamReader(stream))
				{
					while (true)
					{
						string? line = reader.ReadLine();

						if (line == null)
							break;

						uploadedFileStates.Add(FileState.Parse(line));
					}
				}

				uploadedFileStates.Should().BeEquivalentTo(expectedMergedFileStates);

				deletions.Should().HaveCount(RemoteFileStateCache.ConsolidationBatchCountMinimum - 1);

				for (int i = 1; i < RemoteFileStateCache.ConsolidationBatchCountMinimum; i++)
					deletions.Should().Contain("/state/" + i);
			}
		}

		[TestCase(2, int.MaxValue)]
		[TestCase(2, 5000)]
		[TestCase(5, int.MaxValue)]
		[TestCase(5, 10000)]
		[TestCase(5, 5000)]
		[TestCase(5, 2500)]
		public void ConsolidateBatches_should_consolidate_more_batches_when_files_are_larger(int batchCountMinimum, int bytesPerAdditionalBatch)
		{
			// Arrange
			RemoteFileStateCache.ConsolidationBatchCountMinimum = batchCountMinimum;
			RemoteFileStateCache.ConsolidationBatchBytesPerAdditionalBatch = bytesPerAdditionalBatch;

			var faker = new Faker();

			var autoFaker = CreateAutoFaker(faker);

			var batches = new List<List<FileState>>();

			var usedPaths = new HashSet<string>();

			int batchSizeGoal = bytesPerAdditionalBatch;

			if (batchSizeGoal > 4096)
				batchSizeGoal = 4096;

			for (int i = 0; i < batchCountMinimum * 10; i++)
			{
				var batch = new List<FileState>();
				var batchStream = new MemoryStream();
				var batchStreamWriter = new StreamWriter(batchStream) { AutoFlush = true };
				int targetBatchSize = faker.Random.Number(batchSizeGoal / 2, batchSizeGoal);

				while (true)
				{
					var item = autoFaker.Generate<FileState>();

					while (!usedPaths.Add(item.Path))
						item.Path = faker.System.FilePath();

					item = FileState.Parse(item.ToString());

					batch.Add(item);
					batchStreamWriter.WriteLine(item);

					if (batchStream.Length >= targetBatchSize)
						break;
				}

				batches.Add(batch);
			}

			var dummyStorage = new DummyStorage();

			dummyStorage.InitializeWithBatches(batches);

			var parameters = new OperatingParameters();
			var errorLogger = Substitute.For<IErrorLogger>();
			var timer = Substitute.For<ITimer>();
			var cacheActionLog = Substitute.For<ICacheActionLog>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			using (var temporaryFile = new TemporaryFile())
			{
				cacheActionLog.CreateTemporaryCacheActionDataFile().Returns(temporaryFile.Path);

				var uploads = new List<(string ServerPath, byte[] Content)>();
				var deletions = new List<string>();

				remoteStorage.When(x => x.UploadFileDirect(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())).Do(
					x =>
					{
						var serverPath = x.Arg<string>();
						var content = x.Arg<Stream>();

						byte[] contentBytes = new byte[content.Length];

						content.Read(contentBytes, 0, contentBytes.Length);

						uploads.Add((serverPath, contentBytes));
					});

				remoteStorage.When(x => x.DeleteFileDirect(Arg.Any<string>(), Arg.Any<CancellationToken>())).Do(
					x =>
					{
						var serverPath = x.Arg<string>();

						deletions.Add(serverPath);
					});

				var sut = new RemoteFileStateCache(
					parameters,
					errorLogger,
					timer,
					dummyStorage,
					cacheActionLog,
					remoteStorage);

				sut.Start();

				int minimumBatchesToConsolidate = RemoteFileStateCache.ConsolidationBatchCountMinimum;

				int expectedBatchesConsolidated = 0;

				int totalBatchSizes = 0;

				for (int i=0; i < batches.Count; i++)
				{
					int batchSize = dummyStorage.GetBatchFileSize(i + 1);

					totalBatchSizes += batchSize;

					minimumBatchesToConsolidate = RemoteFileStateCache.ConsolidationBatchCountMinimum + (totalBatchSizes + RemoteFileStateCache.ConsolidationBatchBytesPerAdditionalBatch / 2) / RemoteFileStateCache.ConsolidationBatchBytesPerAdditionalBatch;

					expectedBatchesConsolidated++;

					if (expectedBatchesConsolidated >= minimumBatchesToConsolidate)
						break;
				}

				// Act
				sut.ConsolidateBatches();

				// Assert
				sut.DrainActionQueue();

				cacheActionLog.Received(1).CreateTemporaryCacheActionDataFile();

				uploads.Should().HaveCount(1);
				uploads[0].ServerPath.Should().Be("/state/" + expectedBatchesConsolidated);
				uploads[0].Content.Length.Should().BeCloseTo(
					Enumerable.Range(1, expectedBatchesConsolidated).Sum(batchNumber => dummyStorage.GetBatchFileSize(batchNumber)),
					delta: (uint)(expectedBatchesConsolidated + 1));

				deletions.Should().HaveCount(expectedBatchesConsolidated - 1);
				deletions.Should().BeEquivalentTo(Enumerable.Range(1, expectedBatchesConsolidated - 1).Select(batchNumber => "/state/" + batchNumber));
			}
		}
	}
}
