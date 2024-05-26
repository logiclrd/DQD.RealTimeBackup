using System;
using System.Threading;

namespace DeltaQ.RTB.Scan
{
	public interface IInitialBackupOrchestrator
	{
		void PerformInitialBackup(Action<InitialBackupStatus> statusUpdateCallback, CancellationToken cancellationToken);
	}
}
