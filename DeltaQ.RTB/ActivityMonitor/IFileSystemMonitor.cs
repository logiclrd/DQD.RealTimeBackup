using System;

namespace DeltaQ.RTB.ActivityMonitor
{
	public interface IFileSystemMonitor
	{
		event EventHandler<PathUpdate>? PathUpdate;
		event EventHandler<PathMove>? PathMove;

		void Start();
		void Stop();
	}
}

