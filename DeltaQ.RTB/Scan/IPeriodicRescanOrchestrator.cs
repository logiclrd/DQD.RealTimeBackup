using System.Threading;

namespace DeltaQ.RTB.Scan
{
	public interface IPeriodicRescanOrchestrator
	{
		void PerformPeriodicRescan(CancellationToken cancellationToken);
	}
}
