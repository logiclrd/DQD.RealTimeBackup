using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using Bogus;

using FluentAssertions;

using DQD.RealTimeBackup.StateCache;

using DQD.RealTimeBackup.Tests.Support;

namespace DQD.RealTimeBackup.Tests.Fixtures.StateCache
{
	[TestFixture]
	public class CacheActionLogTests
	{
		Faker _faker = new Faker();

		[Test]
		public void EnumerateActionKeys_should_enumerate_keylike_files()
		{
			// Arrange
			using (var dir = new TemporaryDirectory())
			{
				var parameters = new OperatingParameters();

				var sut = new CacheActionLog(parameters);

				sut.ActionQueuePath = dir.Path;

				var keys = new List<long>();

				for (int i=0; i < 10; i++)
				{
					keys.Add(_faker.Random.Long(min: 0));
					File.WriteAllText(sut.GetQueueActionFileName(keys[i]), i.ToString());
				}

				// Act
				var result = sut.EnumerateActionKeys().ToList();

				// Assert
				result.Should().BeEquivalentTo(keys);
			}
		}

		[Test]
		public void GetQueueActionFileName_should_include_key_in_response()
		{
			// Arrange
			var parameters = new OperatingParameters();

			var sut = new CacheActionLog(parameters);

			var key = _faker.Random.Long(min: 0);

			// Act
			var result = sut.GetQueueActionFileName(key);

			// Assert
			result.Should().Contain(key.ToString());
		}

		[Test]
		public void LogAction_should_write_cache_action_to_unique_file_that_sorts_as_most_recent()
		{
			// Arrange
			using (var dir = new TemporaryDirectory())
			{
				var parameters = new OperatingParameters();

				var sut = new CacheActionLog(parameters);

				sut.ActionQueuePath = dir.Path;

				for (int i=0; i < 10; i++)
				{
					var dummyKey = DateTime.UtcNow.Ticks - _faker.Random.Number(min: 1000, max: 100000000);

					var path = sut.GetQueueActionFileName(dummyKey);

					File.WriteAllText(path, "dummy");
				}

				var action = new CacheAction();

				action.CacheActionType = _faker.PickRandom<CacheActionType>();
				action.Path = _faker.System.FilePath();
				action.SourcePath =
					action.CacheActionType switch
					{
						CacheActionType.UploadFile => _faker.System.FilePath(),
						CacheActionType.DeleteFile => null,

						_ => null,
					};

				// Act
				sut.LogAction(action);

				// Assert
				var newestKey = sut.EnumerateActionKeys().Max();

				var filePath = sut.GetQueueActionFileName(newestKey);

				filePath.Should().StartWith(sut.ActionQueuePath + '/');

				var fileContent = File.ReadAllText(filePath);

				CacheAction.Deserialize(fileContent).Should()
					.BeEquivalentTo(action, options => options.Excluding(x => x.ActionFileName));
			}
		}
	}
}
