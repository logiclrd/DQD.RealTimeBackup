using System;

namespace DeltaQ.RTB.ActivityMonitor
{
	public interface IFileSystemMonitor
	{
		event EventHandler<PathUpdate>? PathUpdate;
		event EventHandler<PathMove>? PathMove;
		event EventHandler<PathDelete>? PathDelete;

		void Start();
		void Stop();
	}
}

