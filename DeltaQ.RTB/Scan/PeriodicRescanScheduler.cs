using System.Runtime.CompilerServices;
using System.Threading;

namespace DeltaQ.RTB.Scan
{
	public class PeriodicRescanScheduler : IPeriodicRescanScheduler
		{
		OperatingParameters _parameters;

		IPeriodicRescanOrchestrator _orchestrator;

		Timer? _timer;
		CancellationToken _cancellationToken = CancellationToken.None;

		object _sync = new object();
		bool _isBusy = false;

		public PeriodicRescanScheduler(OperatingParameters parameters, IPeriodicRescanOrchestrator orchestrator)
		{
			_parameters = parameters;

			_orchestrator = orchestrator;
		}

		public void Start(CancellationToken cancellationToken)
		{
			_cancellationToken = cancellationToken;

			_timer = new Timer(
				TimerElapsed,
				state: null,
				dueTime: _parameters.PeriodicRescanInterval,
				period: _parameters.PeriodicRescanInterval);
		}

		public void Stop()
		{
			_timer?.Dispose();
			_timer = null;
		}

		void TimerElapsed(object? state)
		{
			ThreadPool.QueueUserWorkItem(PeriodicRescanWorker, state: null);
		}

		void PeriodicRescanWorker(object? state)
		{
			lock (_sync)
			{
				if (_isBusy)
					return;

				_isBusy = true;

				try
				{
					_orchestrator.PerformPeriodicRescan(_cancellationToken);
				}
				finally
				{
					_isBusy = false;
				}
			}
		}
	}
}
