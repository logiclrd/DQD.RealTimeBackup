using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeltaQ.RTB
{
  public interface IFileAccessNotify : IDisposable
  {
    void MarkPath(string path);
    void MonitorEvents(Action<FileAccessNotifyEvent> eventCallback, CancellationToken cancellationToken);
  }
}

