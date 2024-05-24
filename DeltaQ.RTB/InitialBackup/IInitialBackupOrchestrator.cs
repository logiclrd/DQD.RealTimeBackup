using System;
using System.Threading;

namespace DeltaQ.RTB.InitialBackup
{
	public interface IInitialBackupOrchestrator
	{
		void PerformInitialBackup(Action<InitialBackupStatus> statusUpdateCallback, CancellationToken cancellationToken);
	}
}
