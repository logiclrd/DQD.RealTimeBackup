using System;

namespace DeltaQ.RTB
{
  public interface IFileSystemMonitor
  {
    event EventHandler<PathUpdate>? PathUpdate;
    event EventHandler<PathMove>? PathMove;

    void Start();
    void Stop();
  }
}

