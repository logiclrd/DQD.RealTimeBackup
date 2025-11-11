using System;
using System.Data;
using System.IO;
using System.Text;
using DQD.RealTimeBackup.Utility;
using FluentAssertions;
using NUnit.Framework;

namespace DQD.RealTimeBackup.Tests.Fixtures.Utility
{
	public class UTF8FromTextReaderStreamTests
	{
		static string SourceText = new StreamReader(
			typeof(UTF8FromTextReaderStreamTests).Assembly
			.GetManifestResourceStream("DQD.RealTimeBackup.Tests.Fixtures.Utility.UTF8FromTextReaderStreamTestSourceText.txt")!).ReadToEnd();

		static TextReader CreateSource() => new StringReader(SourceText);

		[TestCase(-1)]
		[TestCase(100)]
		public void Read_should_throw_when_offset_is_out_of_range(int testOffset)
		{
			// Arrange
			var sut = new UTF8FromTextReaderStream(CreateSource());

			byte[] buffer = new byte[100];

			// Act
			Action action = () => sut.Read(buffer, testOffset, 1);

			// Assert
			action.Should().Throw<ArgumentOutOfRangeException>();
		}

		[TestCase(0, -1)]
		[TestCase(0, 101)]
		[TestCase(1, 100)]
		[TestCase(99, 2)]
		[TestCase(99, -5)]
		public void Read_should_throw_when_count_is_out_of_range(int testOffset, int testCount)
		{
			// Arrange
			var sut = new UTF8FromTextReaderStream(CreateSource());

			byte[] buffer = new byte[100];

			// Act
			sut.Read(buffer, testOffset, 1);

			Action action = () => sut.Read(buffer, testOffset, testCount);

			// Assert
			action.Should().Throw<ArgumentOutOfRangeException>();
		}

		[Test]
		public void Read_should_read_partial_conversion_buffer()
		{
			// Arrange
			var sut = new UTF8FromTextReaderStream(CreateSource());

			byte[] buffer = new byte[100];

			// Act
			int numRead = sut.Read(buffer, 0, 100);

			// Assert
			numRead.Should().Be(100);
			sut.ConvertedOffset.Should().Be(100);
		}

		[Test]
		public void Read_should_read_full_conversion_buffer()
		{
			// Arrange
			var sut = new UTF8FromTextReaderStream(CreateSource());

			byte[] buffer = new byte[UTF8FromTextReaderStream.ConvertedBufferSize * 2];

			// Act
			int numRead = sut.Read(buffer, 0, buffer.Length);

			// Assert
			numRead.Should().Be(sut.ConvertedLength);
			sut.ConvertedRemaining.Should().Be(0);
		}

		[Test]
		public void Read_should_read_next_conversion_buffer()
		{
			// Arrange
			var sut = new UTF8FromTextReaderStream(CreateSource());

			byte[] prologueBuffer = new byte[UTF8FromTextReaderStream.ConvertedBufferSize];

			int numRead = sut.Read(prologueBuffer, 0, prologueBuffer.Length);

			if ((numRead != sut.ConvertedLength) || (sut.ConvertedRemaining > 0))
				Assert.Inconclusive();

			byte[] buffer = new byte[UTF8FromTextReaderStream.ConvertedBufferSize];

			// Act
			numRead = sut.Read(buffer, 0, buffer.Length);

			// Assert
			numRead.Should().Be(sut.ConvertedLength);
			buffer.Should().NotBeEquivalentTo(prologueBuffer);
		}

		[Test]
		public void Read_should_convert_entire_source_in_parts_correctly()
		{
			// Arrange
			var expectedBytes = Encoding.UTF8.GetBytes(SourceText);

			var sut = new UTF8FromTextReaderStream(CreateSource());

			byte[] buffer = new byte[expectedBytes.Length];

			var rnd = new Random(100);

			int offset = 0;

			// Act
			while (offset < buffer.Length)
			{
				int remaining = buffer.Length - offset;

				int readLength = rnd.Next(10, 200);

				if (readLength > remaining)
					readLength = remaining;

				readLength = sut.Read(buffer, offset, readLength);

				if (readLength == 0)
					Assert.Fail("Early end of stream");

				offset += readLength;
			}

			// Assert
			buffer.Should().BeEquivalentTo(expectedBytes);
		}

		const int UseSourceLength = -1;
		const int UseConvertedLength = -2;

		class EndOfStreamTestMatrix : System.Collections.IEnumerable
		{
			public System.Collections.IEnumerator GetEnumerator()
			{
				int[] testLengths = [0, 10, 1000, UseSourceLength];
				int[] readLengths = [1, 100, UseConvertedLength, 100_000];

				foreach (var testLength in testLengths)
					foreach (var readLength in readLengths)
						yield return new object[] { testLength, readLength };
			}
		}

		[TestCaseSource(typeof(EndOfStreamTestMatrix))]
		public void Read_should_return_zero_at_end_of_stream(int testLength, int readLength)
		{
			// Arrange
			if (testLength == UseSourceLength)
				testLength = SourceText.Length;

			string effectiveSourceText = SourceText.Substring(0, testLength);

			byte[] expectedBytes = Encoding.UTF8.GetBytes(effectiveSourceText);

			if (readLength == UseConvertedLength)
				readLength = expectedBytes.Length;

			var sut = new UTF8FromTextReaderStream(new StringReader(effectiveSourceText));

			byte[] discard = new byte[expectedBytes.Length];

			sut.ReadExactly(discard);

			byte[] readBuffer = new byte[readLength];

			// Act
			int numRead = sut.Read(readBuffer, 0, readLength);

			// Assert
			numRead.Should().Be(0);
		}
	}
}
