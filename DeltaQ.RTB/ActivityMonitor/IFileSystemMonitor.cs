using System;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.ActivityMonitor
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

