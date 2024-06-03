using NUnit.Framework;

using FluentAssertions;

using DeltaQ.RTB.UserInterface.Controls;

namespace DeltaQ.RTB.UserInterface.Tests.Controls
{
	[TestFixture]
	public class StatisticsListControlTests
	{
		[Test]
		public void InferHeadingFromFieldName_should_handle_single_words()
		{
			// Act
			var result = StatisticsListControl.InferHeadingFromFieldName("Test");

			// Assert
			result.Should().Be("Test");
		}

		[Test]
		public void InferHeadingFromFieldName_should_handle_multiple_words_with_PascalCase()
		{
			// Act
			var result = StatisticsListControl.InferHeadingFromFieldName("ThisIsATest");

			// Assert
			result.Should().Be("This is a test");
		}

		[Test]
		public void InferHeadingFromFieldName_should_handle_multiple_words_with_underscores_separating_words()
		{
			// Act
			var result = StatisticsListControl.InferHeadingFromFieldName("this_is_a_test");

			// Assert
			result.Should().Be("This is a test");
		}

		[Test]
		public void InferHeadingFromFieldName_should_handle_acronyms_embedded_in_PascalCase()
		{
			// Act
			var result = StatisticsListControl.InferHeadingFromFieldName("ThisIsYourDNATest");

			// Assert
			result.Should().Be("This is your DNA test");
		}

		[Test]
		public void InferHeadingFromFieldName_should_handle_acronyms_between_underscores()
		{
			// Act
			var result = StatisticsListControl.InferHeadingFromFieldName("this_is_your_DNA_test");

			// Assert
			result.Should().Be("This is your DNA test");
		}

		[Test]
		public void InferHeadingFromFieldName_should_handle_acronyms_at_start_of_PascalCase()
		{
			// Act
			var result = StatisticsListControl.InferHeadingFromFieldName("ZFSSnapshotCount");

			// Assert
			result.Should().Be("ZFS snapshot count");
		}

		[Test]
		public void InferHeadingFromFieldName_should_handle_acronyms_at_start_of_underscores()
		{
			// Act
			var result = StatisticsListControl.InferHeadingFromFieldName("ZFS_snapshot_count");

			// Assert
			result.Should().Be("ZFS snapshot count");
		}

		[Test]
		public void InferHeadingFromFieldName_should_handle_acronyms_at_end_of_PascalCase()
		{
			// Act
			var result = StatisticsListControl.InferHeadingFromFieldName("ThisServiceUsesHTTPS");

			// Assert
			result.Should().Be("This service uses HTTPS");
		}

		[Test]
		public void InferHeadingFromFieldName_should_handle_acronyms_at_end_of_underscores()
		{
			// Act
			var result = StatisticsListControl.InferHeadingFromFieldName("this_service_uses_HTTPS");

			// Assert
			result.Should().Be("This service uses HTTPS");
		}
	}
}
