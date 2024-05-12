using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

using NUnit.Framework;

using FluentAssertions;

using DeltaQ.RTB.Tests.Support;

namespace DeltaQ.RTB.Tests.Fixtures
{
  [TestFixture]
  public class OpenFileHandlesTests
  {
    [Test]
    public void Enumerate_should_enumerate_open_file_handles()
    {
      // Arrange
      var sut = new OpenFileHandles();

      using (var file = new TemporaryFile())
      {
        using (var stream = File.OpenRead(file.Path))
        {
          // Act
          var result = sut.Enumerate(file.Path).ToList();

          // Assert
          result.Should().HaveCount(1);

          var handle = result.Single();

          handle.ProcessID.Should().Be(Process.GetCurrentProcess().Id);
          handle.CommandName.Should().Be("dotnet");
          handle.FileAccess.Should().Be(FileAccess.Read);
          handle.FileType.Should().Be(FileTypes.RegularFile);
          handle.FileName.Should().Be(file.Path);
        }
      }
    }

    [Test]
    public void Enumerate_should_return_empty_set_when_file_has_no_open_handles()
    {
      // Arrange
      var sut = new OpenFileHandles();

      using (var file = new TemporaryFile())
      {
        // Act
        var result = sut.Enumerate(file.Path).ToList();
      
        // Assert
        result.Should().BeEmpty();
      }
    }
  }
}
