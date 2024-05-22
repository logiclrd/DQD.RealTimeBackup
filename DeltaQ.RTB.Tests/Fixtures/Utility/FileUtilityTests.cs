using NUnit.Framework;

using DeltaQ.RTB.Tests.Support;

using DeltaQ.RTB.Utility;
using Bogus;
using System.IO;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DeltaQ.RTB.Tests.Fixtures.Utility
{
	public class FileUtilityTests
	{
		[TestCase(0)]
		[TestCase(1)]
		[TestCase(10)]
		[TestCase(10000)]
		[TestCase(1048575)]
		[TestCase(1048576)]
		[TestCase(1048577)]
		[TestCase(1048576 * 5)]
		[TestCase(10000000)]
		public void FilesAreEqual_should_return_true_for_equal_files(int fileSize)
		{
			// Arrange
			var faker = new Faker();

			using (var file1 = new TemporaryFile())
			using (var file2 = new TemporaryFile())
			{
				byte[] content = faker.Random.Bytes(fileSize);

				File.WriteAllBytes(file1.Path, content);
				File.WriteAllBytes(file2.Path, content);

				// Act
				var result = FileUtility.FilesAreEqual(file1.Path, file2.Path, CancellationToken.None);

				// Assert
				result.Should().BeTrue();
			}
		}

		[TestCase(0, 1)]
		[TestCase(1, 0)]
		[TestCase(1048575, 1048576)]
		[TestCase(1048576, 1048575)]
		public void FilesAreEqual_should_return_false_when_file_size_does_not_match(int fileSize1, int fileSize2)
		{
			// Arrange
			var faker = new Faker();

			using (var file1 = new TemporaryFile())
			using (var file2 = new TemporaryFile())
			{
				byte[] content = faker.Random.Bytes(Math.Max(fileSize1, fileSize2));

				File.WriteAllBytes(file1.Path, content.Take(fileSize1).ToArray());
				File.WriteAllBytes(file2.Path, content.Take(fileSize2).ToArray());

				// Act
				var result = FileUtility.FilesAreEqual(file1.Path, file2.Path, CancellationToken.None);

				// Assert
				result.Should().BeFalse();
			}
		}

		[TestCase(1, 0, 1)]
		[TestCase(10, 0, 1)]
		[TestCase(10, 9, 1)]
		[TestCase(10, 0, 10)]
		[TestCase(1048576, 10, 1)]
		[TestCase(1048576, 1048575, 1)]
		[TestCase(1048576, 10, 1048576 - 20)]
		public void FilesAreEqual_should_return_false_when_content_does_not_match(int fileSize, int differenceStart, int differenceLength)
		{
			// Arrange
			var faker = new Faker();

			using (var file1 = new TemporaryFile())
			using (var file2 = new TemporaryFile())
			{
				byte[] content = faker.Random.Bytes(fileSize);

				File.WriteAllBytes(file1.Path, content);

				for (int i = 0; i < differenceLength; i++)
					content[differenceStart + i] = faker.Random.Byte();

				File.WriteAllBytes(file2.Path, content);

				// Act
				var result = FileUtility.FilesAreEqual(file1.Path, file2.Path, CancellationToken.None);

				// Assert
				result.Should().BeFalse();
			}
		}

		[Test]
		public void FilesAreEqual_should_be_cancellable()
		{
			// Arrange
			using (var file = new TemporaryFile())
			{
				// Create a chonky file. I believe the filesystem supports sparse files, such that this
				// does not actually incur any I/O -- but it does make the file consist of 100MB of
				// readable 0 bytes.
				using (var fileStream = File.OpenWrite(file.Path))
					fileStream.SetLength(100 * 1048576);

				var cts = new CancellationTokenSource();

				// Act
				Task.Run(
					() =>
					{
						// 10 milliseconds should be short enough that it has not had time
						// to compare all 100MB of the file's zeroes against themselves.
						Thread.Sleep(10);
						cts.Cancel();
					});

				bool result = FileUtility.FilesAreEqual(file.Path, file.Path, cts.Token);

				// Assert
				result.Should().BeFalse();
			}
		}
	}
}
