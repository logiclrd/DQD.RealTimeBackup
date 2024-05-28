using System.Collections.Generic;

using DeltaQ.CommandLineParser;

namespace DeltaQ.RTB.Restore
{
	public class CommandLineArguments()
	{
		[Argument(Switch = "/CONFIG")]
		public string ConfigurationPath = DeltaQ.RTB.CommandLineArguments.DefaultConfigurationPath;

		[Switch("/LISTALLFILES")]
		public bool ListAllFiles;

		[Switch("/RECURSIVE")]
		public bool Recursive;

		[Argument("/LISTDIRECTORY")]
		public List<string> ListDirectory = new List<string>();

		[Argument("/RESTOREFILE")]
		public List<string> RestoreFile = new List<string>();

		[Argument("/RESTOREDIRECTORY")]
		public List<string> RestoreDirectory = new List<string>();

		[Argument("/RESTORETO")]
		public string? RestoreTo = null;

		[Argument("/CATFILE")]
		public string? CatFile = null;

		[Switch(Description = "Output in XML, making it machine-readable. The output is streamed as it arrives, so an XML parser that returns nodes as it gets them can read the output in realtime.")]
		public bool XML = false;

		[Switch("/?")]
		public bool ShowUsage;
	}
}
