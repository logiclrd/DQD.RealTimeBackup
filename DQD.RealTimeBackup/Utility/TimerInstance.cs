using System;

using TimerProvider = System.Threading.Timer;

namespace DQD.RealTimeBackup.Utility
{
	public class TimerInstance : ITimerInstance
	{
		TimerProvider _timer;

		public DateTime DueTime { get; }

		public TimerInstance(DateTime dueTime, TimerProvider timer)
		{
			DueTime = dueTime;

			_timer = timer;
		}

		public static TimerInstance Dummy => new TimerInstance(DateTime.UtcNow, default!);

		public void Dispose()
		{
			_timer?.Dispose();
		}
	}
}

