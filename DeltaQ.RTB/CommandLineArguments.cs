using System.ComponentModel;
using DeltaQ.CommandLineParser;

namespace DeltaQ.RTB
{
#pragma warning disable 649
	public class CommandLineArguments
	{
		[Switch(Description = "Disables most output.")]
		public bool Quiet;
		[Switch(Description = "Enables additional informational output, typically for debugging. Takes precedence over Quiet.")]
		public bool Verbose;

		[Switch(Description = "Disables the fanotify integration, which shuts off realtime change detection.")]
		public bool DisableFAN;
	}
#pragma warning restore 649
}

