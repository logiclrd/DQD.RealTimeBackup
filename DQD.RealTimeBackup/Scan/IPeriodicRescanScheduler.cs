using System;
using System.Threading;

namespace DQD.RealTimeBackup.Scan
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
