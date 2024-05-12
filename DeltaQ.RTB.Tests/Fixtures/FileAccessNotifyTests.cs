using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Bogus;

using FluentAssertions;

using DeltaQ.RTB.Tests.Support;

namespace DeltaQ.RTB.Tests.Fixtures
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
      using (var fan = new FileAccessNotify())
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
          for (int i=0; i < 100; i++)          
            using (var writeStream = File.OpenWrite(Path.Join(testPath, "testfile")))
              writeStream.Write(_faker.Random.Bytes(100), 0, 100);

          sync.WaitOne(TimeSpan.FromSeconds(2));

          // Assert
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
      using (var fan = new FileAccessNotify())
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
          receivedEvent.Should().BeFalse();
        }
        finally
        {
          Directory.Delete(testPath, recursive: true);
        }
      }
    }
  }
}
