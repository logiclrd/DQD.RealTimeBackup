using System;

namespace DeltaQ.RTB
{
	public interface ITimerInstance : IDisposable
	{
		DateTime DueTime { get; }
	}
}

