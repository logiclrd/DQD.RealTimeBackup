using System.Collections.Generic;

using NUnit.Framework;

using NSubstitute;

using Bogus;

using DeltaQ.RTB.FileSystem;

namespace DeltaQ.RTB.Tests.Fixtures.FileSystem
{
	[TestFixture]
	public class SnapshotReferenceTrackerTests
	{
		[TestCase(1)]
		[TestCase(10)]
		public void Release_should_dispose_snapshot_when_the_last_reference_is_released_and_not_sooner(int numberOfReferences)
		{
			// Arrange
			var faker = new Faker();

			var snapshot = Substitute.For<IZFSSnapshot>();

			var sut = new SnapshotReferenceTracker(snapshot);

			// Act & Assert
			var refs = new List<SnapshotReference>();

			for (int i = 0; i < numberOfReferences; i++)
				refs.Add(sut.AddReference(faker.System.FilePath()));

			while (refs.Count > 0)
			{
				snapshot.DidNotReceive().Dispose();

				int refIndex = faker.Random.Int(0, refs.Count - 1);

				refs[refIndex].Dispose();
				refs.RemoveAt(refIndex);
			}

			snapshot.Received().Dispose();
		}
	}
}
