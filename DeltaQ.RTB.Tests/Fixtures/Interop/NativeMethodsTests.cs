using System;
using System.Text;

using NUnit.Framework;

using Bogus;

using FluentAssertions;

using DeltaQ.RTB.Interop;

namespace DeltaQ.RTB.Tests.Fixtures.Interop
{
	[TestFixture]
	public class NativeMethodsTests
	{
		static string Escape(string str)
		{
			const string EscapeChars = " \t\n\\";

			StringBuilder? buffer = null;

			for (int i=0; i < str.Length; i++)
			{
				if (EscapeChars.Contains(str[i]))
				{
					if (buffer == null)
						buffer = new StringBuilder(str, 0, i, str.Length + 20);

					buffer.Append('\\');

					string octal = Convert.ToString((int)str[i], toBase: 8);

					for (int j = octal.Length; j < 3; j++)
						buffer.Append('0');

					buffer.Append(octal);
				}
				else
				{
					if (buffer != null)
						buffer.Append(str[i]);
				}
			}

			if (buffer != null)
				return buffer.ToString();
			else
				return str;
		}

		[Test]
		[Repeat(5)]
		public void DecodeMountInfoEntry_should_decode_mount_entry()
		{
			// Arrange
			var faker = new Faker();

			// 52 30 0:42 / /var/log rw,relatime shared:56 - zfs rpool/ROOT/ubuntu_znaqup/var/log rw,xattr,posixacl,casesensitive

			var mountID = faker.Random.Number();
			int parentMountID = faker.Random.Number();
			int deviceMajor = faker.Random.Number();
			int deviceMinor = faker.Random.Number();
			string root = faker.Random.Bool() ? "/" : faker.System.DirectoryPath() + "/";
			string mountPoint = faker.System.DirectoryPath();
			string options = string.Join(',', faker.Lorem.Words());
			string[] optionalFields = faker.Lorem.Words();
			string fileSystemType = faker.Internet.DomainWord();
			string deviceName = faker.System.DirectoryPath();
			string superblockOptions = string.Join(',', faker.Lorem.Words());

			root = Escape(root);
			mountPoint = Escape(mountPoint);
			options = Escape(options);
			for (int i = 0; i < optionalFields.Length; i++)
				optionalFields[i] = Escape(optionalFields[i]);
			fileSystemType = Escape(fileSystemType);
			deviceName = Escape(deviceName);
			superblockOptions = Escape(superblockOptions);

			string mountInfoEntry =
				$"{mountID} {parentMountID} {deviceMajor}:{deviceMinor} {root} {mountPoint} {options} {string.Join(" ", optionalFields)} - {fileSystemType} {deviceName} {superblockOptions}";

			// Act
			NativeMethods.DecodeMountInfoEntry(
				mountInfoEntry,
				out var deserializedMountID,
				out var deserializedParentMountID,
				out var deserializedDeviceMajor,
				out var deserializedDeviceMinor,
				out var deserializedRoot,
				out var deserializedMountPoint,
				out var deserializedOptions,
				out var deserializedOptionalFields,
				out var deserializedFileSystemType,
				out var deserializedDeviceName,
				out var deserializedSuperblockOptions);

			// Assert
			deserializedMountID.Should().Be(mountID);
			deserializedParentMountID.Should().Be(parentMountID);
			deserializedDeviceMajor.Should().Be(deviceMajor);
			deserializedDeviceMinor.Should().Be(deviceMinor);
			deserializedRoot.Should().Be(root);
			deserializedMountPoint.Should().Be(mountPoint);
			deserializedOptions.Should().Be(options);
			deserializedOptionalFields.Should().BeEquivalentTo(optionalFields);
			deserializedFileSystemType.Should().Be(fileSystemType);
			deserializedDeviceName.Should().Be(deviceName);
			deserializedSuperblockOptions.Should().Be(superblockOptions);;
		}
	}
}
