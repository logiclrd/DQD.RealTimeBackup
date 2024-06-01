using System.Collections.Generic;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Agent
{
	public interface IBackupAgent : IDiagnosticOutput
	{
		void Start();
		void Stop();

		int UploadThreadCount { get; }

		BackupAgentQueueSizes GetQueueSizes();
		public UploadStatus?[] GetUploadThreads();

		int OpenFilesCount { get; }

		void PauseMonitor();
		void UnpauseMonitor(bool processBufferedPaths);

		int CheckPath(string path);
		int CheckPaths(IEnumerable<string> paths);
		void NotifyMove(string fromPath, string toPath);
	}
}
