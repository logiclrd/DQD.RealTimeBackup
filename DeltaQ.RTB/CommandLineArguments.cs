using System.Collections.Generic;

using DeltaQ.RTB.Agent;

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

		[Switch(Description = "Performs an initial backup of everything found in the surface area. Any cached file state is erased and recreated from scratch. After the initial backup is complete, fanotify integration is enabled and continuous backup operation is enabled.")]
		public bool InitialBackupThenMonitor;
		[Switch(Description = "Performs an initial backup of everything found in the surface area, and then exits. Any cached file state is erased and recreated from scratch. This switch implies /DISABLEFAN.")]
		public bool InitialBackupThenExit;

		[Argument(Switch = "/CHECK", Description = "Indicates a path that should be immediately considered for Update or Deletion. Use /MOVE for moves & renames.")]
		public List<string> PathsToCheck = new List<string>();

		[Argument(Switch = "/MOVE", Properties = ["FromPath", "ToPath"],
			Description =
				"Indicates a path that should be immediately considered as having been moved. Note that the file contents are not checked to ensure that they have not " +
				"changed. If the file contents might have changed, you should also indicate the \"to\" path with the /CHECK argument.")]
		public List<MoveAction> PathsToMove = new List<MoveAction>();
	}
#pragma warning restore 649
}

