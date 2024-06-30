using System;
using System.Threading;

using DeltaQ.RTB.Bridge.Notifications;
using DeltaQ.RTB.Diagnostics;

namespace DeltaQ.RTB.Scan
{
	public class PeriodicRescanScheduler : IPeriodicRescanScheduler
	{
		OperatingParameters _parameters;

		IErrorLogger _errorLogger;
		IPeriodicRescanOrchestrator _orchestrator;
		INotificationBus _notificationBus;

		Timer? _timer;

		CancellationToken _upstreamCancellationToken = CancellationToken.None;

		object _sync = new object();
		int _nextRescanNumber = 0;
		int? _currentRescanNumber = null;
		RescanStatus? _rescanStatus;
		CancellationTokenSource? _currentRescanCancellationTokenSource = null;

		public PeriodicRescanScheduler(OperatingParameters parameters, IErrorLogger errorLogger, IPeriodicRescanOrchestrator orchestrator, INotificationBus notificationBus)
		{
			_parameters = parameters;

			_errorLogger = errorLogger;
			_orchestrator = orchestrator;
			_notificationBus = notificationBus;
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
					var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_upstreamCancellationToken);

					var input = new RescanWorkerInput();

					input.RescanNumber = Interlocked.Increment(ref _nextRescanNumber);
					input.CancellationToken = linkedCancellationTokenSource.Token;

					_currentRescanNumber = input.RescanNumber;
					_currentRescanCancellationTokenSource = linkedCancellationTokenSource;

					ThreadPool.QueueUserWorkItem(PeriodicRescanWorker, state: input);

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

		class RescanWorkerInput
		{
			public int RescanNumber;
			public CancellationToken CancellationToken;
		}

		void PeriodicRescanWorker(object? state)
		{
			var input = (RescanWorkerInput)state!;

			int rescanNumber = input.RescanNumber;

			_notificationBus.Post(
				new Notification()
				{
					Event = StateEvent.RescanStarted,
				});

			try
			{
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
					input.CancellationToken);
			}
			catch (Exception e)
			{
				_errorLogger.LogError("Unhandled exception during PerformPeriodicRescan call", exception: e);
			}
			finally
			{
				lock (_sync)
				{
					_currentRescanNumber = null;
					Monitor.PulseAll(_sync);
				}

				_notificationBus.Post(
					new Notification()
					{
						Event = StateEvent.RescanCompleted,
					});
			}
		}
	}
}
