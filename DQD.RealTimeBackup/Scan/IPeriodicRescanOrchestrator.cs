using System;
using System.Threading;

using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.Scan
{
	public interface IPeriodicRescanOrchestrator : IDiagnosticOutput
	{
		void PerformPeriodicRescan(int rescanNumber, Action<RescanStatus> statusUpdateCallback, CancellationToken cancellationToken);
	}
}
