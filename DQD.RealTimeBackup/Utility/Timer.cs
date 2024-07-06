using System;
using System.Collections.Generic;
using System.Threading;

using TimerProvider = System.Threading.Timer;

namespace DQD.RealTimeBackup.Utility
{
	public class Timer : ITimer
	{
		HashSet<TimerInstance> _scheduledTimers = new HashSet<TimerInstance>();

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

				var timerInstance = new TimerInstance(DateTime.UtcNow + delay, provider);

				_scheduledTimers.Add(timerInstance);

				timerInstance.Disposed +=
					(_, _) =>
					{
						_scheduledTimers.Remove(timerInstance);
					};

				return timerInstance;
			}
		}
	}
}

