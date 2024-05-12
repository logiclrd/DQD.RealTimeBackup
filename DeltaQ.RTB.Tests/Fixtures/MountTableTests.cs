using NUnit.Framework;

using FluentAssertions;

namespace DeltaQ.RTB.Tests.Fixtures
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
      firstItem.Type.Should().NotBeNullOrWhiteSpace();

      hasSecondItem.Should().BeTrue();
      secondItem.DeviceName.Should().NotBeNullOrWhiteSpace();
      secondItem.MountPoint.Should().NotBeNullOrWhiteSpace();
      secondItem.Type.Should().NotBeNullOrWhiteSpace();
    }
  }
}

