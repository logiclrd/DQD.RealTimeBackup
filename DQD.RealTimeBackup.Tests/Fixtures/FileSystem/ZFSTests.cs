using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using NSubstitute;

using Bogus;

using FluentAssertions;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.FileSystem;

using DQD.RealTimeBackup.Tests.Support;

namespace DQD.RealTimeBackup.Tests.Fixtures.FileSystem
{
	[TestFixture]
	public class ZFSTests
	{
		[TestCase("version", "zfs")]
		[TestCase("list", "MOUNTPOINT")]
		public void ExecuteZFSCommandOutput_should_work(string command, string shouldContainSubstring)
		{
			// Arrange
			var parameters = new OperatingParameters();

			var errorLogger = Substitute.For<IErrorLogger>();

			var sut = new ZFS(parameters, errorLogger);

			// Act
			var output = sut.ExecuteZFSCommandOutput(command).ToList();

			// Assert
			output.Should().NotBeEmpty();

			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>());

			bool found = false;

			foreach (var line in output)
			{
				found = (line.IndexOf(shouldContainSubstring) >= 0);

				if (found)
					break;
			}

			found.Should().BeTrue();
		}

		[Test]
		public void CreateSnapshot_should_create_snapshot_and_Dispose_should_remove_it()
		{
			if (TestsNativeMethods.geteuid() != 0)
				Assert.Inconclusive();

			// Arrange
			var faker = new Faker();

			var parameters = new OperatingParameters();

			var errorLogger = Substitute.For<IErrorLogger>();

			var zfs = new ZFS(parameters, errorLogger);

			string? testDeviceName = null;
			string? snapshotContainerPath = null;

			foreach (var volume in zfs.EnumerateVolumes())
				if ((volume.MountPoint != null) && volume.MountPoint.StartsWith("/home"))
				{
					testDeviceName = volume.DeviceName!;

					snapshotContainerPath = Path.Combine(volume.MountPoint, ".zfs", "snapshot");
				}

			if ((testDeviceName == null) || (snapshotContainerPath == null))
				Assert.Inconclusive();

			var sut = new ZFS(parameters, errorLogger, testDeviceName);

			string testSnapshotName = faker.Random.Hash();

			string fullSnapshotPath = Path.Combine(snapshotContainerPath, testSnapshotName);

			// Act & Assert
			using (var snapshot = sut.CreateSnapshot(testSnapshotName))
			{
				Directory.GetDirectories(snapshotContainerPath).Should().Contain(fullSnapshotPath);
			}

			Directory.GetDirectories(snapshotContainerPath).Should().NotContain(fullSnapshotPath);

			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>());
		}

		[Test]
		public void FindVolume_should_find_volume()
		{
			// Arrange
			var parameters = new OperatingParameters();

			var errorLogger = Substitute.For<IErrorLogger>();

			var sut = new ZFS(parameters, errorLogger);

			var allVolumes = sut.EnumerateVolumes().ToList();

			// Act & Assert
			foreach (var volume in allVolumes)
				sut.FindVolume(volume.DeviceName!).Should().BeEquivalentTo(
					volume,
					options => options
						.Using<long>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, 1048576))
						.When(info => info.Path.EndsWith("Bytes")));

			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>());
		}

		[Test]
		public void FindVolume_should_throw_when_no_match_is_found()
		{
			// Arrange
			var parameters = new OperatingParameters();

			var errorLogger = Substitute.For<IErrorLogger>();

			var sut = new ZFS(parameters, errorLogger);

			// Act
			var action = () => sut.FindVolume("There is no device by this name.");

			// Assert
			action.Should().Throw<KeyNotFoundException>();

			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>());
		}
		
		[Test]
		public void EnumerateVolumes_should_return_results()
		{
			// Arrange
			var parameters = new OperatingParameters();

			var errorLogger = Substitute.For<IErrorLogger>();

			var sut = new ZFS(parameters, errorLogger);

			// Act
			var results = sut.EnumerateVolumes().ToList();

			// Assert
			results.Should().NotBeEmpty();

			errorLogger.DidNotReceive().LogError(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>());
		}
	}
}
