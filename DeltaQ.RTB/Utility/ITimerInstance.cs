using System;

namespace DeltaQ.RTB.Utility
{
	public interface ITimerInstance : IDisposable
	{
		DateTime DueTime { get; }
	}
}

