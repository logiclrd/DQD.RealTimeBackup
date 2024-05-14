using System;
using System.Threading;

namespace DeltaQ.RTB.Interop
{
	public interface IFileAccessNotify : IDisposable
	{
		void MarkPath(string path);
		void MonitorEvents(Action<FileAccessNotifyEvent> eventCallback, CancellationToken cancellationToken);
	}
}

