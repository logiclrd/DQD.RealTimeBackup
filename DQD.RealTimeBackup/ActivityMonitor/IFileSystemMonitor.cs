using System;

using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.ActivityMonitor
{
	public interface IFileSystemMonitor : IDiagnosticOutput
	{
		event EventHandler<PathUpdate>? PathUpdate;
		event EventHandler<PathMove>? PathMove;
		event EventHandler<PathDelete>? PathDelete;

		void Start();
		void Stop();
	}
}

