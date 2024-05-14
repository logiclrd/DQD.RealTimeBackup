using System;

using NUnit.Framework;

using FluentAssertions;

namespace DeltaQ.RTB.Tests.Fixtures
{
	[TestFixture]
	public class FileTypesUtilityTests
	{
		[TestCase("GDIR", FileTypes.GenericDirectory)]
		[TestCase("VREG", FileTypes.VirtualRegularFile)]
		[TestCase("ax25", FileTypes.AX25Socket)]
		[TestCase("LINK", FileTypes.SymLink)]
		[TestCase("PAS", FileTypes.ProcAs)]
		[TestCase("PCRE", FileTypes.ProcCred)]
		[TestCase("PIPE", FileTypes.Pipe)]
		[TestCase("PODR", FileTypes.ProcObjectDirectory)]
		[TestCase("PTS", FileTypes.DevPts)]
		[TestCase("REG", FileTypes.RegularFile)]
		[TestCase("STSO", FileTypes.StreamSocket)]
		public void Parse_should_parse_file_type_tag(string tag, FileTypes expectedFileType)
		{
			// Act & Assert
			FileTypesUtility.Parse(tag).Should().Be(expectedFileType);
		}
	}
}

