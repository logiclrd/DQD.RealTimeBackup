using System;
using System.Threading;

using TimerProvider = System.Threading.Timer;

namespace DQD.RealTimeBackup.Utility
{
	public class Timer : ITimer
	{
		public ITimerInstance ScheduleAction(DateTime dueTimeUTC, Action action)
			=> ScheduleAction(dueTimeUTC - DateTime.UtcNow, action);

		public ITimerInstance ScheduleAction(TimeSpan delay, Action action)
		{
			if (delay.TotalMilliseconds < 1)
			{
				ThreadPool.QueueUserWorkItem(_ => action());
				return TimerInstance.Dummy;
			}
			else
			{
				var provider = new TimerProvider(
						(_) => action(),
						state: default,
						delay,
						Timeout.InfiniteTimeSpan);

				return new TimerInstance(DateTime.UtcNow + delay, provider);
			}
		}
	}
}

