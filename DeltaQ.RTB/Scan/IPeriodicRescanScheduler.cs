using System.Runtime.CompilerServices;
using System.Threading;

namespace DeltaQ.RTB.Scan
{
	public interface IPeriodicRescanScheduler
	{
		void Start(CancellationToken cancellationToken);
		void Stop();
	}
}
