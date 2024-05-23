using System.Collections.Generic;

namespace DeltaQ.RTB.Agent
{
	public interface IBackupAgent
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
