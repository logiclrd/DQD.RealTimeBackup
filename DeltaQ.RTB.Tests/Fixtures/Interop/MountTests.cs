using System;
using System.IO;

using NUnit.Framework;

using FluentAssertions;

using DeltaQ.RTB.Interop;

namespace DeltaQ.RTB.Tests.Fixtures.Interop
{
	[TestFixture]
	public class MountTests
	{
		[Test]
		public void TestDeviceAccess_should_succeed_for_valid_device()
		{
			// Arrange
			var dummyFilePath = Path.GetTempFileName();

			try
			{
				var sut = new Mount(
					default,
					default,
					default,
					default,
					"/",
					"",
					"",
					Array.Empty<string>(),
					"",
					dummyFilePath,
					default);

				// Act
				var result = sut.TestDeviceAccess();

				// Assert
				result.Should().BeTrue();
			}
			finally
			{
				File.Delete(dummyFilePath);
			}
		}

		[Test]
		public void TestDeviceAccess_should_fail_for_ZFS_device_name()
		{
			// Arrange
			var sut = new Mount(
				default,
				default,
				default,
				default,
				"/",
				"",
				"",
				Array.Empty<string>(),
				"",
				"rpool/ROOT/ubuntu_znaqup",
				default);

			// Act
			var result = sut.TestDeviceAccess();

			// Assert
			result.Should().BeFalse();
		}
	}
}

