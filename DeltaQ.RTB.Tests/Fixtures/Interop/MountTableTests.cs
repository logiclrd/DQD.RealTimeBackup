using NUnit.Framework;

using FluentAssertions;

using DeltaQ.RTB.Interop;

namespace DeltaQ.RTB.Tests.Fixtures.Interop
{
	[TestFixture]
	public class MountTableTests
	{
		[Test]
		public void OpenMountForFileSystem_should_work_with_root()
		{
			// Arrange
			var sut = new MountTable();

			// Act
			var result = sut.OpenMountForFileSystem("/");

			// Assert
			result.FileDescriptor.Should().NotBe(0);
		}

		[Test]
		public void EnumerateMounts_should_return_multiple_entries()
		{
			// Arrange
			var sut = new MountTable();

			// Act
			var enumerable = sut.EnumerateMounts();

			var enumeration = enumerable.GetEnumerator();

			var hasFirstItem = enumeration.MoveNext();
			var firstItem = enumeration.Current;

			var hasSecondItem = enumeration.MoveNext();
			var secondItem = enumeration.Current;

			while (enumeration.MoveNext())
				;

			// Assert
			hasFirstItem.Should().BeTrue();
			firstItem.DeviceName.Should().NotBeNullOrWhiteSpace();
			firstItem.MountPoint.Should().NotBeNullOrWhiteSpace();
			firstItem.FileSystemType.Should().NotBeNullOrWhiteSpace();

			hasSecondItem.Should().BeTrue();
			secondItem.DeviceName.Should().NotBeNullOrWhiteSpace();
			secondItem.MountPoint.Should().NotBeNullOrWhiteSpace();
			secondItem.FileSystemType.Should().NotBeNullOrWhiteSpace();
		}

		[TestCase("", "")]
		[TestCase("simple", "simple")]
		[TestCase("embedded\\134character", "embedded\\character")]
		[TestCase("\\040", " ")]
		[TestCase("\\134", "\\")]
		[TestCase(
			@"\134This\040input\040contains\040all\040of\040the\040characters\040that\040need\040to\040be\040encoded,\040including\040spaces,\011tabs\011,\012newlines\040and\040backslashes:\040\134",
			"\\This input contains all of the characters that need to be encoded, including spaces,\ttabs\t,\nnewlines and backslashes: \\")]
		public void Unescape_should_correctly_unescape_octal_encoded_characters(string encoded, string expectedUnescaped)
		{
			// Act
			string actualUnescaped = MountTable.Unescape(encoded)!;

			// Assert
			actualUnescaped.Should().Be(expectedUnescaped);
		}
	}
}

