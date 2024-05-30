using System;
using System.IO;
using System.Xml.Serialization;

using NUnit.Framework;

using NSubstitute;

using FluentAssertions;

using DeltaQ.RTB.Interop;
using DeltaQ.RTB.SurfaceArea;

namespace DeltaQ.RTB.Tests.Fixtures.SurfaceArea
{
	[TestFixture]
	public class PathFilterTests
	{
		[TestCase(
			"/floobs",
			new[] { "/floobs", "/floobs/", "/floobs/a", "/floobs/a/", "/floobs/ab/", "/floobs/a/b", "/floobs/a/b/", "/floobs/ab/ab/" },
			new[] { "/floob", "/floob/", "/floob/a", "/floobsbox", "/floobbox/", "/floobbox/a" })]
		[TestCase(
			"/f",
			new[] { "/f", "/f/", "/f/a", "/f/a/", "/f/ab/", "/f/a/b", "/f/a/b/", "/f/ab/ab/" },
			new[] { "/floobsbox", "/floobbox/", "/floobbox/a" })]
		[TestCase(
			"/usr/lib",
			new[] { "/usr/lib", "/usr/lib/", "/usr/lib/ls", "/usr/lib/modules/somemodule", "/usr/lib/modules/d/" },
			new[] { "/usr", "/usr/bin", "/usr/libexec" })]
		public void GenerateRegularExpression_with_PathFilterType_Prefix_should_generate_expression_that_matches_prefix(string prefix, string[] shouldMatch, string[] shouldNotMatch)
		{
			// Arrange
			var sut = PathFilter.ForPrefix(prefix, default);

			// Act
			var regex = sut.GenerateRegularExpression();

			// Assert
			foreach (var test in shouldMatch)
				regex.IsMatch(test).Should().BeTrue();
			foreach (var test in shouldNotMatch)
				regex.IsMatch(test).Should().BeFalse();
		}

		[TestCase(
			"a",
			new[] { "/a", "/a/", "/b/a", "/b/a/", "/a/b", "/a/b/", "/bob/a", "/bob/a/", "/a/bob", "/a/bob/", "/cindy/a/b", "/cindy/a/bob", "/c/a/b", "/c/a/b/", "/c/a/bob/" },
			new[] { "/al", "/la", "/lal", "/b/al/" })]
		[TestCase(
			"robert",
			new[] { "/robert", "/robert/", "/lib/robert", "/lib/robert/", "/robert/bin", "/robert/bin/", "/a/robert/b", "/al/robert/bob", "/a/b/robert", "/a/b/robert/" },
			new[] { "/robertd", "/robertdowney", "/testing/brobert", "/testing/bobert", "/testing/roberta" })]
		public void GenerateRegularExpression_with_PathFilterType_Component_should_generate_expression_that_matches_component(string component, string[] shouldMatch, string[] shouldNotMatch)
		{
			// Arrange
			var sut = PathFilter.ForComponent(component, default);

			// Act
			var regex = sut.GenerateRegularExpression();

			// Assert
			foreach (var test in shouldMatch)
				regex.IsMatch(test).Should().BeTrue();
			foreach (var test in shouldNotMatch)
				regex.IsMatch(test).Should().BeFalse();
		}

		[TestCase(
			@"^([a-z0-9_\.\-]+)@([\da-z\.\-]+)\.([a-z\.]{2,5})$",
			new[] { "logic@deltaq.org" },
			new[] { "funky monkey" })]
		[TestCase(
			"employ(|er|ee|ment|ing|able)$",
			new[] { "employ", "employer", "employee", "employment", "employing", "employable" },
			new[] { "employees", "employs", "employed" })]
		public void GenerateRegularExpression_with_PathFilterType_RegularExpression_should_compile_supplied_expression(string expression, string[] shouldMatch, string[] shouldNotMatch)
		{
			// Arrange
			var sut = PathFilter.IncludeRegex(expression);

			// Act
			var regex = sut.GenerateRegularExpression();

			// Assert
			foreach (var test in shouldMatch)
				regex.IsMatch(test).Should().BeTrue();
			foreach (var test in shouldNotMatch)
				regex.IsMatch(test).Should().BeFalse();

			regex.ToString().Should().Be(expression);
		}

		[TestCase(
			@"~/.cache",
			new[] { "/root/.cache", "/root/.cache/file", "/home/username/.cache", "/home/username/.cache/file" },
			new[] { "/.cache", "/.cache/file", "/root/intermediate/.cache", "/root/intermediate/.cache/file", "/home/username/intermediate/.cache", "/home/username/intermediate/.cache/file", "/unrelated/.cache", "/unrelated/.cache/file" })]
		public void GenerateRegularExpression_with_PathFilterType_RegularExpression_should_match_any_home_folder_with_tilde_prefix(string expression, string[] shouldMatch, string[] shouldNotMatch)
		{
			// Arrange
			var userList = Substitute.For<IUserList>();

			var users =
				new User[]
				{
					new User() { UserName = "root", HomePath = "/root" },
					new User() { UserName = "username", HomePath = "/home/username" },
				};

			userList.EnumerateRealUsers().Returns(users);

			lock (typeof(PathFilter))
			{
				PathFilter.UserList = userList;

				var sut = PathFilter.IncludeRegex(expression);

				// Act
				var regex = sut.GenerateRegularExpression();

				Console.WriteLine("Generated regular expression: " + regex);

				// Assert
				foreach (var test in shouldMatch)
					try
					{
						regex.IsMatch(test).Should().BeTrue();
					}
					catch (Exception e)
					{
						throw new Exception("Test object is: " + test, e);
					}

				foreach (var test in shouldNotMatch)
					try
					{
						regex.IsMatch(test).Should().BeFalse();
					}
					catch (Exception e)
					{
						throw new Exception("Test object is: " + test, e);
					}

				regex.ToString().Should().NotBe(expression);
			}
		}

		[TestCase(PathFilterType.Prefix, "/test/prefix", false)]
		[TestCase(PathFilterType.Prefix, "/another/test/prefix", true)]
		[TestCase(PathFilterType.Component, "testcomponent", false)]
		[TestCase(PathFilterType.Component, "anothertestcomponent", true)]
		[TestCase(PathFilterType.RegularExpression, "floobs/bar", false)]
		[TestCase(PathFilterType.RegularExpression, @"\.tmp$", true)]
		public void IXmlSerializable_implementation_should_roundtrip_instances(PathFilterType type, string value, bool shouldExclude)
		{
			// Arrange
			var serializer = new XmlSerializer(typeof(PathFilter));

			var buffer = new MemoryStream();

			var testInput = new PathFilter(type, value, shouldExclude);

			// Act
			serializer.Serialize(buffer, testInput);

			buffer.Position = 0;

			var testOutput = (PathFilter)serializer.Deserialize(buffer)!;

			// Assert
			testOutput.Should().BeEquivalentTo(testInput);
		}
	}
}
