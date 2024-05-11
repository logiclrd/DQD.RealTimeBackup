using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using AutoBogus;

using FluentAssertions;

namespace DeltaQ.RTB.Tests
{
  [TestFixture]
  public class MD5ChecksumTests
  {
    static Random s_rnd = new Random();

    IAutoFaker _faker = AutoFaker.Create();

    [TestCase("", "d41d8cd98f00b204e9800998ecf8427e")]
    [TestCase("hello\n", "b1946ac92492d2347c6235b4d2611184")]
    [TestCase("To condense fact from the vapor of nuance.", "db0d0a118cb460ebc48a4780d6a78064")]
    public void ComputeChecksum_stream_should_return_correct_MD5_checksums(string content, string expectedMD5Sum)
    {
      // Arrange
      var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

      var sut = new MD5Checksum();

      // Act
      var actualMD5Sum = sut.ComputeChecksum(stream);

      // Assert
      actualMD5Sum.Should().Be(expectedMD5Sum);
    }

    [TestCase("", "d41d8cd98f00b204e9800998ecf8427e")]
    [TestCase("hello\n", "b1946ac92492d2347c6235b4d2611184")]
    [TestCase("To condense fact from the vapor of nuance.", "db0d0a118cb460ebc48a4780d6a78064")]
    public void ComputeChecksum_path_should_return_correct_MD5_checksums(string content, string expectedMD5Sum)
    {
      // Arrange
      using (var file = new TemporaryFile())
      {
        File.WriteAllBytes(file.Path, Encoding.UTF8.GetBytes(content));

        var sut = new MD5Checksum();

        // Act
        var actualMD5Sum = sut.ComputeChecksum(file.Path);

        // Assert
        actualMD5Sum.Should().Be(expectedMD5Sum);
      }
    }
  }
}

