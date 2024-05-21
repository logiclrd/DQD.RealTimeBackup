using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using NSubstitute;

using Bogus;

using FluentAssertions;

using NativeMethodsUnderTest = DeltaQ.RTB.Interop.NativeMethods;

using DeltaQ.RTB.ActivityMonitor;
using DeltaQ.RTB.Interop;

namespace DeltaQ.RTB.Tests.Fixtures.ActivityMonitor
{
	[TestFixture]
	public class FileSystemMonitorTests
	{
		Faker _faker = new Faker();

		[Test]
		public void ProcessEvent_should_dispatch_update_event()
		{
			// Arrange
			unsafe
			{
				var parameters = new OperatingParameters();

				var mountTable = Substitute.For<IMountTable>();
				var fileAccessNotify = Substitute.For<IFileAccessNotify>();
				var openByHandleAt = Substitute.For<IOpenByHandleAt>();

				var sut = new FileSystemMonitor(
					parameters,
					mountTable,
					() => fileAccessNotify,
					openByHandleAt);

				var fileSystemID = _faker.Random.Int();
				int fileHandleValue = _faker.Random.Int();
				int mountDescriptor = _faker.Random.Int();
				string path = _faker.System.FilePath();

				byte[] fileHandleBytes = BitConverter.GetBytes(fileHandleValue);

				var evt = new FileAccessNotifyEvent();

				evt.InformationStructures.Add(
					new FileAccessNotifyEventInfo()
					{
						Type = FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName,
						FileSystemID = fileSystemID,
						FileHandle = fileHandleBytes,
						FileName = path,
					});

				evt.Metadata =
					new FileAccessNotifyEventMetadata()
					{
						Mask = FileAccessNotifyEventMask.Modified,
					};

				var receivedPathUpdate = new List<PathUpdate>();
				var receivedPathMove = new List<PathMove>();
				var receivedPathDelete = new List<PathDelete>();

				sut.PathUpdate += (sender, e) => { receivedPathUpdate.Add(e); };
				sut.PathMove += (sender, e) => { receivedPathMove.Add(e); };
				sut.PathDelete += (sender, e) => { receivedPathDelete.Add(e); };

				sut.MountDescriptorByFileSystemID[fileSystemID] = mountDescriptor;

				// Act
				sut.ProcessEvent(evt);

				// Assert
				openByHandleAt.Received().Open(mountDescriptor, fileHandleBytes);

				receivedPathUpdate.Should().HaveCount(1);
				receivedPathMove.Should().BeEmpty();
				receivedPathDelete.Should().BeEmpty();

				var pathUpdate = receivedPathUpdate.Single();

				pathUpdate.Path.Should().Be(path);
			}
		}

		[Test]
		public void ProcessEvent_should_dispatch_move_events()
		{
			// Arrange
			unsafe
			{
				var parameters = new OperatingParameters();

				var mountTable = Substitute.For<IMountTable>();
				var fileAccessNotify = Substitute.For<IFileAccessNotify>();
				var openByHandleAt = Substitute.For<IOpenByHandleAt>();

				var sut = new FileSystemMonitor(
					parameters,
					mountTable,
					() => fileAccessNotify,
					openByHandleAt);

				var fileSystemIDFrom = _faker.Random.Int();
				int fileHandleValueFrom = _faker.Random.Int();
				int mountDescriptorFrom = _faker.Random.Int();
				var fileSystemIDTo = _faker.Random.Int();
				int fileHandleValueTo = _faker.Random.Int();
				int mountDescriptorTo = _faker.Random.Int();
				string pathFrom = _faker.System.FilePath();
				string pathTo = _faker.System.FilePath();

				byte[] fileHandleBytesFrom = BitConverter.GetBytes(fileHandleValueFrom);
				byte[] fileHandleBytesTo = BitConverter.GetBytes(fileHandleValueTo);

				var evt = new FileAccessNotifyEvent();

				evt.Metadata =
					new FileAccessNotifyEventMetadata()
					{
						Mask = FileAccessNotifyEventMask.ChildMoved,
					};

				evt.InformationStructures.Add(
					new FileAccessNotifyEventInfo()
					{
						Type = FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_From,
						FileSystemID = fileSystemIDFrom,
						FileHandle = fileHandleBytesFrom,
						FileName = pathFrom,
					});

				evt.InformationStructures.Add(
					new FileAccessNotifyEventInfo()
					{
						Type = FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_To,
						FileSystemID = fileSystemIDTo,
						FileHandle = fileHandleBytesTo,
						FileName = pathTo,
					});

				var receivedPathUpdate = new List<PathUpdate>();
				var receivedPathMove = new List<PathMove>();
				var receivedPathDelete = new List<PathDelete>();

				sut.PathUpdate += (sender, e) => { receivedPathUpdate.Add(e); };
				sut.PathMove += (sender, e) => { receivedPathMove.Add(e); };
				sut.PathDelete += (sender, e) => { receivedPathDelete.Add(e); };

				sut.MountDescriptorByFileSystemID[fileSystemIDFrom] = mountDescriptorFrom;
				sut.MountDescriptorByFileSystemID[fileSystemIDTo] = mountDescriptorTo;

				// Act
				sut.ProcessEvent(evt);

				// Assert
				openByHandleAt.Received().Open(mountDescriptorFrom, fileHandleBytesFrom);
				openByHandleAt.Received().Open(mountDescriptorTo, fileHandleBytesTo);
				openByHandleAt.ReceivedCalls().Should().HaveCount(2);

				receivedPathUpdate.Should().BeEmpty();
				receivedPathMove.Should().HaveCount(1);
				receivedPathDelete.Should().BeEmpty();

				var pathMove = receivedPathMove.Single();

				pathMove.PathFrom.Should().Be(pathFrom);
				pathMove.PathTo.Should().Be(pathTo);
			}
		}

		[Test]
		public void ProcessEvent_should_dispatch_delete_events()
		{
			// Arrange
			unsafe
			{
				var parameters = new OperatingParameters();

				var mountTable = Substitute.For<IMountTable>();
				var fileAccessNotify = Substitute.For<IFileAccessNotify>();
				var openByHandleAt = Substitute.For<IOpenByHandleAt>();

				var sut = new FileSystemMonitor(
					parameters,
					mountTable,
					() => fileAccessNotify,
					openByHandleAt);

				var fileSystemID = _faker.Random.Int();
				int fileHandleValue = _faker.Random.Int();
				int mountDescriptor = _faker.Random.Int();
				string path = _faker.System.FilePath();

				byte[] fileHandleBytes = BitConverter.GetBytes(fileHandleValue);

				var evt = new FileAccessNotifyEvent();

				evt.Metadata =
					new FileAccessNotifyEventMetadata()
					{
						Mask = FileAccessNotifyEventMask.ChildDeleted,
					};

				evt.InformationStructures.Add(
					new FileAccessNotifyEventInfo()
					{
						Type = FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName,
						FileSystemID = fileSystemID,
						FileHandle = fileHandleBytes,
						FileName = path,
					});

				var receivedPathUpdate = new List<PathUpdate>();
				var receivedPathMove = new List<PathMove>();
				var receivedPathDelete = new List<PathDelete>();

				sut.PathUpdate += (sender, e) => { receivedPathUpdate.Add(e); };
				sut.PathMove += (sender, e) => { receivedPathMove.Add(e); };
				sut.PathDelete += (sender, e) => { receivedPathDelete.Add(e); };

				sut.MountDescriptorByFileSystemID[fileSystemID] = mountDescriptor;

				// Act
				sut.ProcessEvent(evt);

				// Assert
				openByHandleAt.Received().Open(mountDescriptor, fileHandleBytes);

				receivedPathUpdate.Should().BeEmpty();
				receivedPathMove.Should().BeEmpty();
				receivedPathDelete.Should().HaveCount(1);

				var pathDelete = receivedPathDelete.Single();

				pathDelete.Path.Should().Be(path);
			}
		}

		[Test]
		public void SetUpFANotify_should_open_all_physical_mounts_including_ZFS()
		{
			// Arrange
			var parameters = new OperatingParameters();

			var mountTable = Substitute.For<IMountTable>();
			var fileAccessNotify = Substitute.For<IFileAccessNotify>();
			var openByHandleAt = Substitute.For<IOpenByHandleAt>();

			var sut = new FileSystemMonitor(
				parameters,
				mountTable,
				() => fileAccessNotify,
				openByHandleAt);

			var openMountPoints = new Dictionary<string, IMountHandle>();

			mountTable.OpenMountForFileSystem(Arg.Any<string>()).Returns(
				x =>
				{
					string mountPoint = x.Arg<string>();

					int fd = _faker.Random.Int();
					long fsid = _faker.Random.Long();

					var handle = Substitute.For<IMountHandle>();

					handle.FileDescriptor.Returns(fd);
					handle.FileSystemID.Returns(fsid);

					openMountPoints[mountPoint] = handle;

					return handle;
				});

			var mounts = new List<IMount>();
			var goodMounts = new List<IMount>();

			// Conventional mounts
			for (int i = 0; i < 10; i++)
			{
				string devNode = _faker.System.DirectoryPath();
				string mountPoint = _faker.System.DirectoryPath();

				var mount = Substitute.For<IMount>();

				mount.DeviceName.Returns(devNode);
				mount.Root.Returns("/");
				mount.MountPoint.Returns(mountPoint);
				mount.FileSystemType.Returns("ext4");
				mount.Options.Returns("rw");

				mount.TestDeviceAccess().Returns(true);

				mounts.Add(mount);
				goodMounts.Add(mount);
			}

			// Virtual device mounts
			for (int i = 0; i < 10; i++)
			{
				string devNode = _faker.Internet.DomainWord();
				string mountPoint = _faker.System.DirectoryPath();
				string type = _faker.Internet.DomainWord();
				bool access = _faker.Random.Bool();

				if (type == "zfs")
					type = "notzfs";

				var mount = Substitute.For<IMount>();

				mount.DeviceName.Returns(devNode);
				mount.Root.Returns("/");
				mount.MountPoint.Returns(mountPoint);
				mount.FileSystemType.Returns(type);
				mount.Options.Returns("rw");

				mount.TestDeviceAccess().Returns(access);

				mounts.Add(mount);
			}

			// ZFS mounts
			for (int i = 0; i < 10; i++)
			{
				string devNode = "rpool/ROOT/" + _faker.System.DirectoryPath();
				string mountPoint = _faker.System.DirectoryPath();

				var mount = Substitute.For<IMount>();

				mount.DeviceName.Returns(devNode);
				mount.Root.Returns("/");
				mount.MountPoint.Returns(mountPoint);
				mount.FileSystemType.Returns("zfs");
				mount.Options.Returns("rw");

				mount.TestDeviceAccess().Returns(false);

				mounts.Add(mount);
				goodMounts.Add(mount);
			}

			mountTable.EnumerateMounts().Returns(mounts);

			sut.InitializeFileAccessNotify();

			// Act
			sut.SetUpFANotify();

			// Assert
			var markedPaths = fileAccessNotify.ReceivedCalls()
				.Where(call => call.GetMethodInfo().Name == nameof(fileAccessNotify.MarkPath))
				.Select(call => call.GetArguments().Single()!.ToString())
				.ToList();

			markedPaths.Should().Contain("/");
			openMountPoints.Keys.Should().Contain("/");

			var openRootMount = openMountPoints["/"];

			sut.MountDescriptorByFileSystemID[openRootMount.FileSystemID].Should().Be(openRootMount.FileDescriptor);

			foreach (var mount in goodMounts)
			{
				markedPaths.Should().Contain(mount.MountPoint);
				openMountPoints.Keys.Should().Contain(mount.MountPoint);

				var openMount = openMountPoints[mount.MountPoint];

				sut.MountDescriptorByFileSystemID[openMount.FileSystemID].Should().Be(openMount.FileDescriptor);
			}
		}

		[Test]
		public void MonitorFileActivity_should_run_FileAccessNotify_MonitorEvents_until_shutdown_is_signalled()
		{
			// Arrange
			var parameters = new OperatingParameters();

			var mountTable = Substitute.For<IMountTable>();
			var fileAccessNotify = Substitute.For<IFileAccessNotify>();
			var openByHandleAt = Substitute.For<IOpenByHandleAt>();

			var sut = new FileSystemMonitor(
				parameters,
				mountTable,
				() => fileAccessNotify,
				openByHandleAt);

			fileAccessNotify
				.When(x => x.MonitorEvents(Arg.Any<Action<FileAccessNotifyEvent>>(), Arg.Any<CancellationToken>()))
				.Do(
					x =>
					{
						var token = x.Arg<CancellationToken>();

						token.WaitHandle.WaitOne();
					});

			bool haveStopped = false;

			TimeSpan delayBeforeStop = TimeSpan.FromMilliseconds(250);
			TimeSpan allowableStopTime = TimeSpan.FromMilliseconds(50);

			// Act & Assert
			var stopwatch = new Stopwatch();

			stopwatch.Start();

			Task.Run(
				() =>
				{
					Thread.Sleep(delayBeforeStop);
					haveStopped = true;
					sut.Stop();
				});

			haveStopped.Should().BeFalse();

			sut.MonitorFileActivityThreadProc();

			stopwatch.Stop();

			haveStopped.Should().BeTrue();

			stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(delayBeforeStop);
			stopwatch.Elapsed.Should().BeLessThan(delayBeforeStop + allowableStopTime);
		}
	}
}
