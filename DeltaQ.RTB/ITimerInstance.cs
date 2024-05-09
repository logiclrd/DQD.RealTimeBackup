using System;

public interface ITimerInstance : IDisposable
{
  DateTime DueTime { get; }
}

