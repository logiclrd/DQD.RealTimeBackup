using System;
using System.Threading;

using TimerProvider = System.Threading.Timer;

namespace DQD.RealTimeBackup.Utility
{
	public class Timer : ITimer
	{
		public ITimerInstance ScheduleAction(TimeSpan delay, Action action)
			=> ScheduleAction(DateTime.UtcNow + delay, action);

		public ITimerInstance ScheduleAction(DateTime dueTimeUTC, Action action)
		{
			var provider = new TimerProvider(
					(_) => action(),
					state: default,
					dueTimeUTC - DateTime.UtcNow,
					Timeout.InfiniteTimeSpan);

			return new TimerInstance(dueTimeUTC, provider);
		}
	}
}

