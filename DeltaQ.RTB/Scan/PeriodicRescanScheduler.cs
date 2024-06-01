using System;
using System.Threading;

namespace DeltaQ.RTB.Scan
{
	public class PeriodicRescanScheduler : IPeriodicRescanScheduler
	{
		OperatingParameters _parameters;

		IPeriodicRescanOrchestrator _orchestrator;

		Timer? _timer;

		CancellationToken _upstreamCancellationToken = CancellationToken.None;

		object _sync = new object();
		int _nextRescanNumber = 0;
		int? _currentRescanNumber = null;
		RescanStatus? _rescanStatus;
		CancellationTokenSource? _currentRescanCancellationTokenSource = null;

		public PeriodicRescanScheduler(OperatingParameters parameters, IPeriodicRescanOrchestrator orchestrator)
		{
			_parameters = parameters;

			_orchestrator = orchestrator;
		}

		public void Start(CancellationToken cancellationToken)
		{
			_upstreamCancellationToken = cancellationToken;

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
			PerformRescanNow();
		}

		public PerformRescanResponse PerformRescanNow()
		{
			var response = new PerformRescanResponse();

			lock (_sync)
			{
				if (_currentRescanNumber is int rescanNumber)
				{
					response.RescanNumber = rescanNumber;
					response.AlreadyRunning = true;
				}
				else
				{
					_currentRescanNumber = Interlocked.Increment(ref _nextRescanNumber);
					_currentRescanCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_upstreamCancellationToken);

					ThreadPool.QueueUserWorkItem(PeriodicRescanWorker, state: _currentRescanCancellationTokenSource.Token);

					response.RescanNumber = _currentRescanNumber.Value;
					response.AlreadyRunning = false;

					Monitor.PulseAll(_sync);
				}
			}

			return response;
		}

		public void CancelRescan()
		{
			lock (_sync)
			{
				if (_currentRescanCancellationTokenSource != null)
					_currentRescanCancellationTokenSource.Cancel();
			}
		}

		public RescanStatus? GetRescanStatus(bool wait)
		{
			if (wait && _currentRescanNumber.HasValue)
			{
				lock (_sync)
					Monitor.Wait(_sync, TimeSpan.FromSeconds(5));
			}

			return _rescanStatus;
		}

		void PeriodicRescanWorker(object? state)
		{
			int rescanNumber = _currentRescanNumber ?? -1;

			try
			{
				var token = (CancellationToken)state!;

				_orchestrator.PerformPeriodicRescan(
					rescanNumber,
					updatedStatus =>
					{
						lock (_sync)
						{
							_rescanStatus = updatedStatus;
							Monitor.PulseAll(_sync);
						}
					},
					token);
			}
			finally
			{
				lock (_sync)
				{
					_currentRescanNumber = null;
					Monitor.PulseAll(_sync);
				}
			}
		}
	}
}
