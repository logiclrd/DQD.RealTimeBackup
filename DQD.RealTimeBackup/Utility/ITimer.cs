using System;

namespace DQD.RealTimeBackup.Utility
{
	public interface ITimer
	{
		ITimerInstance ScheduleAction(TimeSpan delay, Action action);
		ITimerInstance ScheduleAction(DateTime time, Action action);
	}
}

