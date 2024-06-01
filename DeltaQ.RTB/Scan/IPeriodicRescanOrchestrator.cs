using System;
using System.Threading;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Scan
{
	public interface IPeriodicRescanOrchestrator : IDiagnosticOutput
	{
		void PerformPeriodicRescan(int rescanNumber, Action<RescanStatus> statusUpdateCallback, CancellationToken cancellationToken);
	}
}
