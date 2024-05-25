using System.Collections.Generic;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Agent
{
	public interface IBackupAgent : IDiagnosticOutput
	{
		void Start();
		void Stop();

		BackupAgentQueueSizes GetQueueSizes();
		int OpenFilesCount { get; }

		void PauseMonitor();
		void UnpauseMonitor();

		int CheckPath(string path);
		int CheckPaths(IEnumerable<string> paths);
		void NotifyMove(string fromPath, string toPath);
	}
}
