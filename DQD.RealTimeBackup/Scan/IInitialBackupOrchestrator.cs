using System;
using System.Threading;

namespace DQD.RealTimeBackup.Scan
{
	public interface IInitialBackupOrchestrator
	{
		void PerformInitialBackup(Action<InitialBackupStatus> statusUpdateCallback, CancellationToken cancellationToken);
	}
}
