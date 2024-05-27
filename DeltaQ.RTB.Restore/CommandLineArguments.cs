using System.Collections.Generic;

using DeltaQ.CommandLineParser;

namespace DeltaQ.RTB.Restore
{
	public class CommandLineArguments()
	{
		[Argument(Switch = "/CONFIG")]
		public string ConfigurationPath = DeltaQ.RTB.CommandLineArguments.DefaultConfigurationPath;

		[Switch]
		public bool ListAllFiles;

		[Switch]
		public bool Recursive;

		[Argument]
		public List<string> ListDirectory = new List<string>();

		[Argument]
		public List<string> RestoreFile = new List<string>();

		[Argument]
		public List<string> RestoreDirectory = new List<string>();

		[Argument]
		public string? RestoreTo = null;

		[Argument]
		public string? CatFile = null;

		[Switch(Description = "Output in XML, making it machine-readable. The output is streamed as it arrives, so an XML parser that returns nodes as it gets them can read the output in realtime.")]
		public bool XML = false;
	}
}
