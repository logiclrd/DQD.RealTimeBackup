using System;
using System.IO;

using NUnit.Framework;

using FluentAssertions;

namespace DeltaQ.RTB.Tests.Fixtures
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
				var sut = new Mount(dummyFilePath, "", default, default, default, default);

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
			var sut = new Mount("rpool/ROOT/ubuntu_znaqup", "", default, default, default, default);

			// Act
			var result = sut.TestDeviceAccess();

			// Assert
			result.Should().BeFalse();
		}
	}
}

