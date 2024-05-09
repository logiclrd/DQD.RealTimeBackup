using System;

using TimerProvider = System.Threading.Timer;

public class TimerInstance : ITimerInstance
{
  TimerProvider _timer;

  public DateTime DueTime { get; }

  public TimerInstance(DateTime dueTime, TimerProvider timer)
  {
    DueTime = dueTime;

    _timer = timer;
  }

  public void Dispose()
  {
    _timer.Dispose();
  }
}

