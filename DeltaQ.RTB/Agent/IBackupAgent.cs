using System.Collections.Generic;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Agent
{
	public interface IBackupAgent : IDiagnosticOutput
	{
		void Start();
		void Stop();

		BackupAgentQueueSizes GetQueueSizes();

		void PauseMonitor();
		void UnpauseMonitor();

		void CheckPath(string path);
		void CheckPaths(IEnumerable<string> paths);
		void NotifyMove(string fromPath, string toPath);
	}
}
