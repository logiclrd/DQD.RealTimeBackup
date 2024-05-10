using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using AutoBogus;

using FluentAssertions;

[TestFixture]
public class FileStateTests
{
  static Random s_rnd = new Random();

  IAutoFaker _faker = AutoFaker.Create();

  [Test]
  public void FromFile_should_read_file_properties()
  {
    // Arrange
    using (var file = new TemporaryFile())
    {
      const string Content = "hello\n";
      const string ExpectedMD5Sum = "b1946ac92492d2347c6235b4d2611184";

      File.WriteAllText(file.Path, Content);

      var lastWriteTimeUtc = new DateTime(1970, 1, 1) + TimeSpan.FromMilliseconds(s_rnd.Next() & 0x7FFFFFFF);

      File.SetLastWriteTimeUtc(file.Path, lastWriteTimeUtc);

      // Act
      var fileState = FileState.FromFile(file.Path);

      // Assert
      fileState.Path.Should().Be(file.Path);
      fileState.FileSize.Should().Be(Content.Length);
      fileState.LastModifiedUTC.Should().Be(lastWriteTimeUtc);
      fileState.Checksum.Should().Be(ExpectedMD5Sum);
    }
  }

  [Test]
  public void IsMatch_should_succeed_on_matching_file()
  {
    // Arrange
    using (var file = new TemporaryFile())
    {
      const string Content = "hello\n";
      const string ExpectedMD5Sum = "b1946ac92492d2347c6235b4d2611184";

      File.WriteAllText(file.Path, Content);

      var lastWriteTimeUtc = new DateTime(1970, 1, 1) + TimeSpan.FromMilliseconds(s_rnd.Next() & 0x7FFFFFFF);

      File.SetLastWriteTimeUtc(file.Path, lastWriteTimeUtc);

      var sut = new FileState();

      sut.Path = file.Path;
      sut.FileSize = Content.Length;
      sut.LastModifiedUTC = lastWriteTimeUtc;
      sut.Checksum = ExpectedMD5Sum;

      // Act
      var result = sut.IsMatch();

      // Assert
      result.Should().BeTrue();
    }
  }

  [Test]
  public void IsMatch_should_fail_when_file_size_does_not_match()
  {
    // Arrange
    using (var file = new TemporaryFile())
    {
      const string Content = "hello\n";
      const string ExpectedMD5Sum = "b1946ac92492d2347c6235b4d2611184";

      File.WriteAllText(file.Path, Content);

      var lastWriteTimeUtc = new DateTime(1970, 1, 1) + TimeSpan.FromMilliseconds(s_rnd.Next() & 0x7FFFFFFF);

      File.SetLastWriteTimeUtc(file.Path, lastWriteTimeUtc);

      var sut = new FileState();

      sut.Path = file.Path;
      sut.FileSize = Content.Length + 5; // Deliberately not matching
      sut.LastModifiedUTC = lastWriteTimeUtc;
      sut.Checksum = ExpectedMD5Sum;

      // Act
      var result = sut.IsMatch();

      // Assert
      result.Should().BeFalse();
    }
  }

  [Test]
  public void IsMatch_should_fail_when_checksum_does_not_match()
  {
    // Arrange
    using (var file = new TemporaryFile())
    {
      const string Content = "hello\n";
      const string NotExpectedMD5Sum = "c1946ac92492a2347c6235b4d2611184";

      File.WriteAllText(file.Path, Content);

      var lastWriteTimeUtc = new DateTime(1970, 1, 1) + TimeSpan.FromMilliseconds(s_rnd.Next() & 0x7FFFFFFF);

      File.SetLastWriteTimeUtc(file.Path, lastWriteTimeUtc);

      var sut = new FileState();

      sut.Path = file.Path;
      sut.FileSize = Content.Length;
      sut.LastModifiedUTC = lastWriteTimeUtc;
      sut.Checksum = NotExpectedMD5Sum;

      // Act
      var result = sut.IsMatch();

      // Assert
      result.Should().BeFalse();
    }
  }

  [TestCase("", "d41d8cd98f00b204e9800998ecf8427e")]
  [TestCase("hello\n", "b1946ac92492d2347c6235b4d2611184")]
  [TestCase("To condense fact from the vapor of nuance.", "db0d0a118cb460ebc48a4780d6a78064")]
  public void ComputeChecksum_should_return_correct_MD5_checksums(string content, string expectedMD5Sum)
  {
    // Arrange
    var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

    // Act
    var actualMD5Sum = FileState.ComputeChecksum(stream);

    // Assert
    actualMD5Sum.Should().Be(expectedMD5Sum);
  }

  [Repeat(25)]
  public void ToString_and_Parse_should_roundtrip_instances()
  {
    // Arrange
    var fileState = AutoFaker.Generate<FileState>();

    // Act
    string serialized = fileState.ToString();
    var deserialized = FileState.Parse(serialized);

    // Assert
    Console.WriteLine(deserialized);

    deserialized.Should().BeEquivalentTo(fileState);
  }
}

