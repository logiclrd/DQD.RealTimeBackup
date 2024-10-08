using System;
using System.Collections.Generic;

using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.Agent
{
	public interface IBackupAgent : IDiagnosticOutput
	{
		void Start();
		void Stop();

		int UploadThreadCount { get; }

		BackupAgentQueueSizes GetQueueSizes();
		UploadStatus?[] GetUploadThreads();
		void CancelUpload(int uploadThreadIndex);

		int OpenFilesCount { get; }

		void PauseMonitor();
		void UnpauseMonitor(bool processBufferedPaths);

		int CheckPath(string path);
		int CheckPaths(IEnumerable<string> paths);
		void NotifyMove(string fromPath, string toPath);
	}
}
