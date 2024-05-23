using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using NSubstitute;

using FluentAssertions;

using DeltaQ.RTB.Interop;
using DeltaQ.RTB.SurfaceArea;

namespace DeltaQ.RTB.Tests.Fixtures.SurfaceArea
{
	[TestFixture]
	public class SurfaceAreaImplementationTests
	{
		IMount CreateMount(string deviceName, string root, string fileSystemType, string mountPoint, bool accessible)
		{
			var mount = Substitute.For<IMount>();

			mount.DeviceName.Returns(deviceName);
			mount.Root.Returns(root);
			mount.FileSystemType.Returns(fileSystemType);
			mount.MountPoint.Returns(mountPoint);

			mount.TestDeviceAccess().Returns(accessible);

			return mount;
		}

		[Test]
		public void BuildDefault_should_ignore_non_root_mounts()
		{
			// Arrange
			var parameters = new OperatingParameters();
			var mountTable = Substitute.For<IMountTable>();

			var mounts = new List<IMount>();

			mounts.Add(CreateMount("/a", "/", "x", "/", accessible: true));
			mounts.Add(CreateMount("/b", "/home", "y", "/home", accessible: true));
			mounts.Add(CreateMount("/c", "/", "z", "/root", accessible: true));
			mounts.Add(CreateMount("rpool/FLOOBS/username_token", "/", "zfs", "/home/username", accessible: true));

			parameters.MonitorFileSystemTypes.AddRange(mounts.Select(m => m.FileSystemType));

			mountTable.EnumerateMounts().Returns(mounts);

			var sut = new SurfaceAreaImplementation(parameters, mountTable);

			// Act
			sut.BuildDefault();

			// Assert
			sut.Mounts.Should().HaveCount(3);

			sut.Mounts.Should().Contain(mounts[0]);
			sut.Mounts.Should().Contain(mounts[2]);
			sut.Mounts.Should().Contain(mounts[3]);
		}

		[Test]
		public void BuildDefault_should_ignore_undesired_filesystem_types()
		{
			// Arrange
			var parameters = new OperatingParameters();
			var mountTable = Substitute.For<IMountTable>();

			var mounts = new List<IMount>();

			mounts.Add(CreateMount("/a", "/", "x", "/", accessible: true));
			mounts.Add(CreateMount("/b", "/", "y", "/home", accessible: true));
			mounts.Add(CreateMount("/c", "/", "z", "/root", accessible: true));
			mounts.Add(CreateMount("rpool/FLOOBS/username_token", "/", "zfs", "/home/username", accessible: true));

			parameters.MonitorFileSystemTypes.Add(mounts[0].FileSystemType);
			parameters.MonitorFileSystemTypes.Add(mounts[3].FileSystemType);

			mountTable.EnumerateMounts().Returns(mounts);

			var sut = new SurfaceAreaImplementation(parameters, mountTable);

			// Act
			sut.BuildDefault();

			// Assert
			sut.Mounts.Should().HaveCount(2);

			sut.Mounts.Should().Contain(mounts[0]);
			sut.Mounts.Should().Contain(mounts[3]);
		}

		[Test]
		public void BuildDefault_should_detect_device_mounted_multiple_times()
		{
			// Arrange
			var parameters = new OperatingParameters();
			var mountTable = Substitute.For<IMountTable>();

			var mounts = new List<IMount>();

			mounts.Add(CreateMount("/a", "/", "x", "/", accessible: true));
			mounts.Add(CreateMount("/a", "/", "y", "/home", accessible: true));
			mounts.Add(CreateMount("/c", "/", "z", "/root", accessible: true));
			mounts.Add(CreateMount("rpool/FLOOBS/username_token", "/", "zfs", "/home/username", accessible: true));

			parameters.MonitorFileSystemTypes.AddRange(mounts.Select(m => m.FileSystemType));

			mountTable.EnumerateMounts().Returns(mounts);

			var sut = new SurfaceAreaImplementation(parameters, mountTable);

			// Act
			Action action = () => sut.BuildDefault();

			// Assert
			action.Should().Throw<Exception>().WithMessage("More than one mount point refers to device /a*");
		}

		[Test]
		public void BuildDefault_should_accept_device_mounted_multiple_times_with_matching_preferred_mount_point()
		{
			// Arrange
			var parameters = new OperatingParameters();
			var mountTable = Substitute.For<IMountTable>();

			var mounts = new List<IMount>();

			mounts.Add(CreateMount("/a", "/", "x", "/", accessible: true));
			mounts.Add(CreateMount("/b", "/", "y", "/home", accessible: true));
			mounts.Add(CreateMount("/b", "/", "z", "/root", accessible: true));
			mounts.Add(CreateMount("rpool/FLOOBS/username_token", "/", "zfs", "/home/username", accessible: true));

			parameters.MonitorFileSystemTypes.AddRange(mounts.Select(m => m.FileSystemType));

			parameters.PreferredMountPoints.Add("/home");

			mountTable.EnumerateMounts().Returns(mounts);

			var sut = new SurfaceAreaImplementation(parameters, mountTable);

			// Act
			sut.BuildDefault();

			// Assert
			sut.Mounts.Should().HaveCount(3);

			sut.Mounts.Should().Contain(mounts[0]);
			sut.Mounts.Should().Contain(mounts[1]);
			sut.Mounts.Should().Contain(mounts[3]);
		}

		[Test]
		public void BuildDefault_should_detect_device_mounted_multiple_times_with_multiple_matching_preferred_mount_points()
		{
			// Arrange
			var parameters = new OperatingParameters();
			var mountTable = Substitute.For<IMountTable>();

			var mounts = new List<IMount>();

			mounts.Add(CreateMount("/a", "/", "x", "/", accessible: true));
			mounts.Add(CreateMount("/b", "/", "y", "/home", accessible: true));
			mounts.Add(CreateMount("/b", "/", "z", "/root", accessible: true));
			mounts.Add(CreateMount("rpool/FLOOBS/username_token", "/", "zfs", "/home/username", accessible: true));

			parameters.MonitorFileSystemTypes.AddRange(mounts.Select(m => m.FileSystemType));

			parameters.PreferredMountPoints.Add("/home");
			parameters.PreferredMountPoints.Add("/root");

			mountTable.EnumerateMounts().Returns(mounts);

			var sut = new SurfaceAreaImplementation(parameters, mountTable);

			// Act
			Action action = () => sut.BuildDefault();

			// Assert
			action.Should().Throw<Exception>().WithMessage("More than one mount point refers to device /b*");
		}

		[Test]
		public void BuildDefault_should_ignore_inaccessible_mount_points_except_ZFS()
		{
			// Arrange
			var parameters = new OperatingParameters();
			var mountTable = Substitute.For<IMountTable>();

			var mounts = new List<IMount>();

			mounts.Add(CreateMount("/a", "/", "x", "/", accessible: true));
			mounts.Add(CreateMount("/b", "/", "y", "/home", accessible: true));
			mounts.Add(CreateMount("/c", "/", "z", "/root", accessible: false));
			mounts.Add(CreateMount("rpool/FLOOBS/username_token", "/", "zfs", "/home/username", accessible: true));

			parameters.MonitorFileSystemTypes.AddRange(mounts.Select(m => m.FileSystemType));

			mountTable.EnumerateMounts().Returns(mounts);

			var sut = new SurfaceAreaImplementation(parameters, mountTable);

			// Act
			sut.BuildDefault();

			// Assert
			sut.Mounts.Should().HaveCount(3);

			sut.Mounts.Should().Contain(mounts[0]);
			sut.Mounts.Should().Contain(mounts[1]);
			sut.Mounts.Should().Contain(mounts[3]);
		}

		[Test]
		public void BuildDefault_should_ignore_empty_mount_points()
		{
			// Arrange
			var parameters = new OperatingParameters();
			var mountTable = Substitute.For<IMountTable>();

			var mounts = new List<IMount>();

			mounts.Add(CreateMount("/a", "/", "x", "/", accessible: true));
			mounts.Add(CreateMount("/b", "/", "y", "/home", accessible: true));
			mounts.Add(CreateMount("/c", "/", "z", "", accessible: true));
			mounts.Add(CreateMount("rpool/FLOOBS/username_token", "/", "zfs", "/home/username", accessible: true));

			parameters.MonitorFileSystemTypes.AddRange(mounts.Select(m => m.FileSystemType));

			mountTable.EnumerateMounts().Returns(mounts);

			var sut = new SurfaceAreaImplementation(parameters, mountTable);

			// Act
			sut.BuildDefault();

			// Assert
			sut.Mounts.Should().HaveCount(3);

			sut.Mounts.Should().Contain(mounts[0]);
			sut.Mounts.Should().Contain(mounts[1]);
			sut.Mounts.Should().Contain(mounts[3]);
		}
	}
}
