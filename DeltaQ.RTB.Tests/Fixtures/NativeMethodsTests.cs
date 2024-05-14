using System;
using System.IO;
using System.Runtime.InteropServices;
//using System.Text;

using NUnit.Framework;

using Bogus;

using FluentAssertions;

using DeltaQ.RTB.Tests.Support;

using NativeMethods = DeltaQ.RTB.NativeMethods;

namespace DeltaQ.RTB.Tests.Fixtures
{
	[TestFixture]
	public class NativeMethodsTests
	{
		[Test]
		[Repeat(5)]
		public void DecodeMountEntry_should_decode_mount_entry()
		{
			// Arrange
			var faker = new Faker();

			var fsname = faker.Internet.DomainWord();
			var mountPoint = faker.System.DirectoryPath();
			var type = faker.Internet.DomainWord();
			var options = faker.Lorem.Letter(num: faker.Random.Int(2, 5));
			var frequency = faker.Random.Int(1, 100);
			var passNumber = faker.Random.Int(1, 10);

			using (var fsnameUnmanaged = new UnmanagedString(fsname))
			using (var mountPointUnmanaged = new UnmanagedString(mountPoint))
			using (var typeUnmanaged = new UnmanagedString(type))
			using (var optionsUnmanaged = new UnmanagedString(options))
			{
				var buffer = new MemoryStream();

				var writer = new BinaryWriter(buffer);

				if (IntPtr.Size == 4)
				{
					writer.Write((int)fsnameUnmanaged.DangerousRawPointer);
					writer.Write((int)mountPointUnmanaged.DangerousRawPointer);
					writer.Write((int)typeUnmanaged.DangerousRawPointer);
					writer.Write((int)optionsUnmanaged.DangerousRawPointer);
				}
				else
				{
					writer.Write((long)fsnameUnmanaged.DangerousRawPointer);
					writer.Write((long)mountPointUnmanaged.DangerousRawPointer);
					writer.Write((long)typeUnmanaged.DangerousRawPointer);
					writer.Write((long)optionsUnmanaged.DangerousRawPointer);
				}

				writer.Write(frequency);
				writer.Write(passNumber);

				writer.Flush();

				byte[] serializedMountPoint = buffer.ToArray();

				unsafe
				{
					fixed (byte* serializedMountPointAddress = &serializedMountPoint[0])
					{
						// Act
						NativeMethods.DecodeMountEntry(
							(IntPtr)serializedMountPointAddress,
							out var deserializedFSName,
							out var deserializedMountPoint,
							out var deserializedType,
							out var deserializedOptions,
							out var deserializedFrequency,
							out var deserializedPassNumber);

						// Assert
						deserializedFSName.Should().Be(fsname);
						deserializedMountPoint.Should().Be(mountPoint);
						deserializedType!.Should().Be(type);
						deserializedOptions!.Should().Be(options);
						deserializedFrequency.Should().Be(frequency);
						deserializedPassNumber.Should().Be(passNumber);
					}
				}
			}
		}
	}
}
