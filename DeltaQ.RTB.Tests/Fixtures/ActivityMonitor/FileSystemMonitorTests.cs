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
				var mountTable = Substitute.For<IMountTable>();
				var fileAccessNotify = Substitute.For<IFileAccessNotify>();
				var openByHandleAt = Substitute.For<IOpenByHandleAt>();

				var sut = new FileSystemMonitor(
					mountTable,
					() => fileAccessNotify,
					openByHandleAt);

				var fileSystemID = _faker.Random.Int();
				int fileHandleValue = _faker.Random.Int();
				int mountDescriptor = _faker.Random.Int();
				string path = _faker.System.FilePath();

				var evt = new FileAccessNotifyEvent();

				var evtAdditionalData = new MemoryStream();

				var writer = new BinaryWriter(evtAdditionalData);

				writer.Write((byte)NativeMethodsUnderTest.FAN_EVENT_INFO_TYPE_FID); // infoType
				writer.Write((byte)0); // padding
				writer.Write((short)8); // length

				writer.Write((long)fileSystemID); // fsid

				// fileHandle
				writer.Write(4); // file handle byte count
				writer.Write(fileHandleValue); // file handle bytes

				var evtAdditionalDataBytes = evtAdditionalData.ToArray();

				fixed (byte* evtAdditionalDataPointer = &evtAdditionalDataBytes[0])
				{
					evt.Metadata =
						new FileAccessNotifyEventMetadata()
						{
							Mask = NativeMethodsUnderTest.FAN_MODIFY,
						};

					evt.AdditionalDataLength = (int)evtAdditionalData.Length;
					evt.AdditionalData = (IntPtr)evtAdditionalDataPointer;

					var fileHandleAddress = evt.AdditionalData + 12;

					var receivedPathUpdate = new List<PathUpdate>();
					var receivedPathMove = new List<PathMove>();

					sut.PathUpdate += (sender, e) => { receivedPathUpdate.Add(e); };
					sut.PathMove += (sender, e) => { receivedPathMove.Add(e); };

					sut.MountDescriptorByFileSystemID[fileSystemID] = mountDescriptor;

					openByHandleAt.Open(Arg.Any<int>(), Arg.Any<IntPtr>()).Returns(
						x =>
						{
							var openHandle = Substitute.For<IOpenHandle>();

							openHandle.ReadLink().Returns(path);

							return openHandle;
						});

					// Act
					sut.ProcessEvent(evt);

					// Assert
					openByHandleAt.Received().Open(mountDescriptor, fileHandleAddress);

					receivedPathMove.Should().BeEmpty();
					receivedPathUpdate.Should().HaveCount(1);

					var pathUpdate = receivedPathUpdate.Single();

					pathUpdate.Path.Should().Be(path);
					pathUpdate.UpdateType.Should().Be(UpdateType.ContentUpdated);
				}
			}
		}

		[TestCase(NativeMethodsUnderTest.FAN_MOVED_FROM)]
		[TestCase(NativeMethodsUnderTest.FAN_MOVED_TO)]
		public void ProcessEvent_should_dispatch_move_events(int mask)
		{
			// Arrange
			unsafe
			{
				var mountTable = Substitute.For<IMountTable>();
				var fileAccessNotify = Substitute.For<IFileAccessNotify>();
				var openByHandleAt = Substitute.For<IOpenByHandleAt>();

				var sut = new FileSystemMonitor(
					mountTable,
					() => fileAccessNotify,
					openByHandleAt);

				var fileSystemID = _faker.Random.Int();
				int fileHandleValue = _faker.Random.Int();
				int mountDescriptor = _faker.Random.Int();
				string path = _faker.System.FilePath();

				var evt = new FileAccessNotifyEvent();

				var evtAdditionalData = new MemoryStream();

				var writer = new BinaryWriter(evtAdditionalData);

				writer.Write((byte)NativeMethodsUnderTest.FAN_EVENT_INFO_TYPE_FID); // infoType
				writer.Write((byte)0); // padding
				writer.Write((short)8); // length

				writer.Write((long)fileSystemID); // fsid

				// fileHandle
				writer.Write(4); // file handle byte count
				writer.Write(fileHandleValue); // file handle bytes

				var evtAdditionalDataBytes = evtAdditionalData.ToArray();

				fixed (byte* evtAdditionalDataPointer = &evtAdditionalDataBytes[0])
				{
					evt.Metadata =
						new FileAccessNotifyEventMetadata()
						{
							Mask = mask,
						};

					evt.AdditionalDataLength = (int)evtAdditionalData.Length;
					evt.AdditionalData = (IntPtr)evtAdditionalDataPointer;

					var fileHandleAddress = evt.AdditionalData + 12;

					var receivedPathUpdate = new List<PathUpdate>();
					var receivedPathMove = new List<PathMove>();

					sut.PathUpdate += (sender, e) => { receivedPathUpdate.Add(e); };
					sut.PathMove += (sender, e) => { receivedPathMove.Add(e); };

					sut.MountDescriptorByFileSystemID[fileSystemID] = mountDescriptor;

					openByHandleAt.Open(Arg.Any<int>(), Arg.Any<IntPtr>()).Returns(
						x =>
						{
							var openHandle = Substitute.For<IOpenHandle>();

							openHandle.ReadLink().Returns(path);

							return openHandle;
						});

					// Act
					sut.ProcessEvent(evt);

					// Assert
					openByHandleAt.Received().Open(mountDescriptor, fileHandleAddress);

					receivedPathUpdate.Should().BeEmpty();
					receivedPathMove.Should().HaveCount(1);

					var pathMove = receivedPathMove.Single();

					pathMove.ContainerPath.Should().Be(path);
					pathMove.MoveType.Should().Be(
						mask switch
						{
							NativeMethodsUnderTest.FAN_MOVED_FROM => MoveType.From,
							NativeMethodsUnderTest.FAN_MOVED_TO => MoveType.To,
							_ => throw new ArgumentOutOfRangeException(),
						});
				}
			}
		}

		[Test]
		public void SetUpFANotify_should_open_all_physical_mounts_including_ZFS()
		{
			// Arrange
			var mountTable = Substitute.For<IMountTable>();
			var fileAccessNotify = Substitute.For<IFileAccessNotify>();
			var openByHandleAt = Substitute.For<IOpenByHandleAt>();

			var sut = new FileSystemMonitor(
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
				mount.MountPoint.Returns(mountPoint);
				mount.Type.Returns("ext4");
				mount.Options.Returns("rw");
				mount.Frequency.Returns(1);
				mount.PassNumber.Returns(2);

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
				mount.MountPoint.Returns(mountPoint);
				mount.Type.Returns(type);
				mount.Options.Returns("rw");
				mount.Frequency.Returns(1);
				mount.PassNumber.Returns(2);

				mount.TestDeviceAccess().Returns(access);

				mounts.Add(mount);
			}

			// ZFS mounts
			for (int i = 0; i < 10; i++)
			{
				string devNode = _faker.System.DirectoryPath();
				string mountPoint = "rpool/ROOT/" + _faker.System.DirectoryPath();

				var mount = Substitute.For<IMount>();

				mount.DeviceName.Returns(devNode);
				mount.MountPoint.Returns(mountPoint);
				mount.Type.Returns("zfs");
				mount.Options.Returns("rw");
				mount.Frequency.Returns(1);
				mount.PassNumber.Returns(2);

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
			var mountTable = Substitute.For<IMountTable>();
			var fileAccessNotify = Substitute.For<IFileAccessNotify>();
			var openByHandleAt = Substitute.For<IOpenByHandleAt>();

			var sut = new FileSystemMonitor(
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
			Task.Run(
				() =>
				{
					Thread.Sleep(delayBeforeStop);
					haveStopped = true;
					sut.Stop();
				});

			haveStopped.Should().BeFalse();

			var stopwatch = new Stopwatch();

			stopwatch.Start();
			sut.MonitorFileActivity();
			stopwatch.Stop();

			haveStopped.Should().BeTrue();

			stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(delayBeforeStop);
			stopwatch.Elapsed.Should().BeLessThan(delayBeforeStop + allowableStopTime);
		}
	}
}
