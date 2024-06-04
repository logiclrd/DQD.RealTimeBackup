using System;
using System.IO;

using DeltaQ.RTB.Bridge.Messages;
using DeltaQ.RTB.Bridge.Notifications;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Diagnostics
{
	public class ErrorLogger : DiagnosticOutputBase, IErrorLogger
	{
		OperatingParameters _parameters;

		INotificationBus? _notificationBus;

		object _sync = new object();
		string? _errorLogFilePath;
		bool _diagnosticOutputDisconnected = false;

		public ErrorLogger(OperatingParameters parameters, INotificationBus notificationBus)
		{
			_notificationBus = notificationBus;
			_parameters = parameters;
		}

		public ErrorLogger(string errorLogFilePath)
		{
			_parameters = new OperatingParameters(); // Default instance, shouldn't be used
			_errorLogFilePath = errorLogFilePath;
		}

		public ILoggedError LogError(string context, Exception? exception = null)
		{
			lock (_sync)
			{
				var buffer = new StringWriter();

				try
				{
					if (exception != null)
					{
						buffer.WriteLine("{0}: {1}", exception.GetType().Name, exception.Message);
						buffer.WriteLine();
					}

					var timestamp = DateTime.Now;

					buffer.WriteLine("{0:yyyy-MM-dd HH:mm:ss}", timestamp);
					buffer.WriteLine("{0:yyyy-MM-dd HH:mm:ss} UTC", timestamp.ToUniversalTime());
					buffer.WriteLine();
					buffer.WriteLine(context);

					if (exception != null)
						buffer.Write(exception);
				}
				catch {}

				string serializedErrorText = buffer.ToString();

				try
				{
					using (var writer = new StreamWriter(_errorLogFilePath ?? _parameters.ErrorLogFilePath, append: true))
					{
						writer.WriteLine("----------");
						writer.Write(serializedErrorText);
					}
				}
				catch {}

				if (!_diagnosticOutputDisconnected)
					OnDiagnosticOutput(Importance.Normal, "----------\n" + serializedErrorText + "----------\n");

				_notificationBus?.Post(
						new Notification()
						{
							ErrorMessage = context,
							Error = (exception == null) ? null : new ErrorInfo(exception),
						});

				return new LoggedError(serializedErrorText);
			}
		}

		public void DisconnectDiagnosticOutput()
		{
			_diagnosticOutputDisconnected = true;
		}
	}
}
