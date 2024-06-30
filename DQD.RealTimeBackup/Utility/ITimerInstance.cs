using System;

namespace DQD.RealTimeBackup.Utility
{
	public interface ITimerInstance : IDisposable
	{
		DateTime DueTime { get; }
	}
}

