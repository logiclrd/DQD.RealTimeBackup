using System.Threading;

namespace DeltaQ.RTB.Scan
{
	public interface IPeriodicRescanScheduler
	{
		void Start(CancellationToken cancellationToken);
		void Stop();

		PerformRescanResponse PerformRescanNow();
		void CancelRescan();
		RescanStatus? GetRescanStatus(bool wait);
	}
}
