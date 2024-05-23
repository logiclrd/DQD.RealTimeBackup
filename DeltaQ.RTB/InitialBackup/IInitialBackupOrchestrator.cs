using System;

using DeltaQ.RTB.Agent;

namespace DeltaQ.RTB.InitialBackup
{
	public interface IInitialBackupOrchestrator
	{
		void PerformInitialBackup(Action<InitialBackupStatus> statusUpdateCallback);
	}
}
