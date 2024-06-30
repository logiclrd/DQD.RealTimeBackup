using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using NSubstitute;

using Bogus;

using FluentAssertions;

using DQD.RealTimeBackup.Tests.Support;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.Interop;

namespace DQD.RealTimeBackup.Tests.Fixtures.Interop
{
	[TestFixture]
	public class FileAccessNotifyTests
	{
		Faker _faker = new Faker();

		[Test]
		public void MarkPath_should_enable_collecting_events_for_path()
		{
			if (TestsNativeMethods.geteuid() != 0)
				Assert.Inconclusive();

			// Arrange
			var parameters = new OperatingParameters();

			var errorLogger = Substitute.For<IErrorLogger>();

			using (var fan = new FileAccessNotify(parameters, errorLogger))
			{
				bool receivedEvent = false;

				var cancellationSource = new CancellationTokenSource();

				string testPath = Path.Join(
					Environment.CurrentDirectory,
					"testtemp",
					_faker.System.FileName());

				Directory.CreateDirectory(testPath);

				int myProcessID = System.Diagnostics.Process.GetCurrentProcess().Id;

				fan.MarkPath(testPath);

				var sync = new ManualResetEvent(initialState: false);

				Task.Run(
					() =>
					{
						sync.Set();

						fan.MonitorEvents(
							evt =>
							{
								if (evt.Metadata.ProcessID == myProcessID)
								{
									receivedEvent = true;
									sync.Set();
								}
							},
							cancellationSource.Token);
					});

				sync.WaitOne();
				sync.Reset();

				try
				{
					// Act
					for (int i = 0; i < 100; i++)
						using (var writeStream = File.OpenWrite(Path.Join(testPath, "testfile")))
							writeStream.Write(_faker.Random.Bytes(100), 0, 100);

					sync.WaitOne(TimeSpan.FromSeconds(2));

					// Assert
					errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

					receivedEvent.Should().BeTrue();
				}
				finally
				{
					Directory.Delete(testPath, recursive: true);
				}
			}
		}

		[Test]
		public void Events_should_not_be_collected_for_unmarked_paths()
		{
			if (TestsNativeMethods.geteuid() != 0)
				Assert.Inconclusive();

			// Arrange
			var parameters = new OperatingParameters();

			var errorLogger = Substitute.For<IErrorLogger>();

			using (var fan = new FileAccessNotify(parameters, errorLogger))
			{
				bool receivedEvent = false;

				var cancellationSource = new CancellationTokenSource();

				FileStream? writeStream = null;

				Task.Run(
					() => fan.MonitorEvents(
						evt =>
						{
							if ((writeStream != null)
							 && (evt.Metadata.FileDescriptor == (int)writeStream.SafeFileHandle.DangerousGetHandle()))
								receivedEvent = true;
						},
						cancellationSource.Token));

				string testPath = "/tmp/" + _faker.System.FileName();
				string testPath2 = "/tmp/" + _faker.System.FileName();

				Directory.CreateDirectory(testPath);

				try
				{
					// Act
					fan.MarkPath(testPath);

					writeStream = File.OpenWrite(testPath2);
					writeStream.Write(_faker.Random.Bytes(100), 0, 100);

					Thread.Sleep(50);

					// Assert
					errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Exception>());

					receivedEvent.Should().BeFalse();
				}
				finally
				{
					Directory.Delete(testPath, recursive: true);
				}
			}
		}

		[TestCase(0, 0, 0)]
		[TestCase(0, 0, 1)]
		[TestCase(0, 0, 10)]
		[TestCase(0, 20, 10)]
		[TestCase(0, 0, -1)]
		[TestCase(0, 20, -1)]
		[TestCase(30, 0, 0)]
		[TestCase(30, 0, 1)]
		[TestCase(30, 0, 10)]
		[TestCase(30, 20, 10)]
		[TestCase(30, 0, -1)]
		[TestCase(30, 20, -1)]
		public void CreateSubStream_should_encapsulate_subset_of_bytes_in_parent_UnmanagedMemoryStream(int position, int offset, int length)
		{
			// Arrange
			var random = new Random();

			byte[] data = new byte[500];

			random.NextBytes(data);

			unsafe
			{
				fixed (byte *dataPtr = &data[0])
				{
					var parentStream = new UnmanagedMemoryStream(dataPtr, data.Length);

					parentStream.Position = position;

					var expectedLength = (length >= 0) ? length : (int)(parentStream.Length - parentStream.Position - offset);
					var expectedBytes = new byte[expectedLength];

					Array.Copy(data, position + offset, expectedBytes, 0, expectedLength);

					// Act
					var subStream = FileAccessNotify.CreateSubStream(parentStream, offset, length);

					// Assert
					subStream.Position.Should().Be(0);
					subStream.Length.Should().Be(expectedLength);

					byte[] actualBytes = new byte[subStream.Length];

					int bytesRead = subStream.Read(actualBytes, 0, actualBytes.Length);

					bytesRead.Should().Be(expectedLength);

					actualBytes.Should().BeEquivalentTo(expectedBytes);
				}
			}
		}

		[Test]
		public void ReadStringAtUnmanagedMemoryStreamCurrentPosition_should_read_string()
		{
			// Arrange
			var faker = new Faker();

			var buffer = new MemoryStream();

			var writer = new BinaryWriter(buffer);

			for (int i=0; i < 5; i++)
				writer.Write(faker.Random.Int());

			var expectedString = faker.Random.Utf16String();

			writer.Write(Encoding.UTF8.GetBytes(expectedString));
			writer.Write((byte)0);

			writer.Write(Encoding.UTF8.GetBytes(faker.Lorem.Sentence()));

			byte[] bytes = buffer.ToArray();

			unsafe
			{
				fixed (byte *bytePtr = &bytes[0])
				{
					var stream = new UnmanagedMemoryStream(bytePtr, bytes.Length);

					stream.Position = 20;

					// Act
					var actualString = FileAccessNotify.ReadStringAtUnmanagedMemoryStreamCurrentPosition(stream);

					// Assert
					stream.Position.Should().Be(20);
					actualString.Should().Be(expectedString);
				}
			}
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(2)]
		[TestCase(3)]
		public void ParseEventInfoStructures_should_handle_event_with_no_info_structures(int extraBytes)
		{
			// Arrange
			var faker = new Faker();

			var buffer = new MemoryStream();

			var writer = new BinaryWriter(buffer);

			for (int i=0; i < 5; i++)
				writer.Write(faker.Random.Int());

			byte[] data = buffer.ToArray();

			unsafe
			{
				fixed (byte *dataPtr = &data[0])
				{
					var eventStream = new UnmanagedMemoryStream(dataPtr, data.Length);

					eventStream.Position = eventStream.Length - extraBytes;

					var expectedEventStreamPosition = eventStream.Position;

					// Act
					var infoStructures = FileAccessNotify.ParseEventInfoStructures(eventStream).ToList();

					// Assert
					infoStructures.Should().BeEmpty();
					eventStream.Position.Should().Be(expectedEventStreamPosition);
				}
			}
		}

		enum StructureType
		{
			Bare,
			Handle,
			FileName,
		}

		[TestCase(1, 0, 0)]
		[TestCase(2, 0, 0)]
		[TestCase(3, 0, 0)]
		[TestCase(0, 1, 0)]
		[TestCase(0, 2, 0)]
		[TestCase(0, 3, 0)]
		[TestCase(0, 0, 1)]
		[TestCase(0, 0, 2)]
		[TestCase(0, 0, 3)]
		[TestCase(3, 3, 3)]
		[TestCase(10, 10, 10)]
		public void ParseEventInfoStructures_should_parse_all_types_of_info_structures(int bareCount, int handleCount, int fileNameCount)
		{
			// Arrange
			var faker = new Faker();

			var buffer = new MemoryStream();

			var writer = new BinaryWriter(buffer);

			for (int i=0; i < 5; i++)
				writer.Write(faker.Random.Int());

			long infoStructureStartPosition = buffer.Length;

			var structuresToGenerate = new List<StructureType>();

			for (int i=0; i < bareCount; i++)
				structuresToGenerate.Add(StructureType.Bare);
			for (int i=0; i < handleCount; i++)
				structuresToGenerate.Add(StructureType.Handle);
			for (int i=0; i < fileNameCount; i++)
				structuresToGenerate.Add(StructureType.FileName);

			var expectedInfoStructures = new FileAccessNotifyEventInfo[structuresToGenerate.Count];

			var types =
				new Dictionary<StructureType, FileAccessNotifyEventInfoType[]>()
				{
					{
						StructureType.Bare,
						new[]
						{
							FileAccessNotifyEventInfoType.ProcessFileDescriptor,
						}
					},
					{
						StructureType.Handle,
						new[]
						{
							FileAccessNotifyEventInfoType.FileIdentifier,
							FileAccessNotifyEventInfoType.ContainerIdentifier,
						}
					},
					{
						StructureType.FileName,
						new[]
						{
							FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName,
							FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_From,
							FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_To,
						}
					}
				};

			while (structuresToGenerate.Count > 0)
			{
				int i = expectedInfoStructures.Length - structuresToGenerate.Count;

				int j = faker.Random.Int(0, structuresToGenerate.Count - 1);

				var structureToGenerate = structuresToGenerate[j];

				structuresToGenerate.RemoveAt(j);

				expectedInfoStructures[i] = new FileAccessNotifyEventInfo();

				expectedInfoStructures[i].Type = faker.PickRandom(types[structureToGenerate]);

				short structureLength =
					1 + // type
					1 + // padding
					2; // length

				if ((structureToGenerate == StructureType.Handle)
				 || (structureToGenerate == StructureType.FileName))
				{
					structureLength +=
						8 + // fsid
						4 + // file_handle handle bytes
						4 + // file_handle type
						8; // file_handle handle

					expectedInfoStructures[i].FileSystemID = faker.Random.Long();

					var fileHandleBuffer = new MemoryStream();

					var fileHandleWriter = new BinaryWriter(fileHandleBuffer);

					fileHandleWriter.Write(8); // length of file_handle handle
					fileHandleWriter.Write(faker.Random.Int()); // file_handle type
					fileHandleWriter.Write(faker.Random.Long()); // 8 bytes for file_handle handle

					expectedInfoStructures[i].FileHandle = fileHandleBuffer.ToArray();

					if (structureToGenerate == StructureType.FileName)
					{
						expectedInfoStructures[i].FileName = faker.System.FilePath();

						structureLength += (short)(Encoding.UTF8.GetByteCount(expectedInfoStructures[i].FileName!) + 1);
					}
				}

				writer.Write((byte)expectedInfoStructures[i].Type);
				writer.Write((byte)0);
				writer.Write(structureLength);

				if ((structureToGenerate == StructureType.Handle)
				 || (structureToGenerate == StructureType.FileName))
				{
					writer.Write(expectedInfoStructures[i].FileSystemID);
					writer.Write(expectedInfoStructures[i].FileHandle!);

					if (structureToGenerate == StructureType.FileName)
					{
						writer.Write(Encoding.UTF8.GetBytes(expectedInfoStructures[i].FileName!));
						writer.Write((byte)0);
					}
				}
			}

			byte[] data = buffer.ToArray();

			unsafe
			{
				fixed (byte *dataPtr = &data[0])
				{
					var eventStream = new UnmanagedMemoryStream(dataPtr, data.Length);

					eventStream.Position = infoStructureStartPosition;

					// Act
					var infoStructures = FileAccessNotify.ParseEventInfoStructures(eventStream).ToList();

					// Assert
					infoStructures.Should().HaveCount(expectedInfoStructures.Length);

					for (int i=0; i < infoStructures.Count; i++)
						infoStructures[i].Should().BeEquivalentTo(expectedInfoStructures[i]);

					eventStream.Position.Should().Be(eventStream.Length);
				}
			}
		}
	}
}
