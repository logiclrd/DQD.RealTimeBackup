using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using ReactiveUI;

using DeltaQ.RTB.Bridge;
using DeltaQ.RTB.Bridge.Messages;
using DeltaQ.RTB.Bridge.Notifications;
using DeltaQ.RTB.UserInterface.Notifications;

namespace DeltaQ.RTB.UserInterface
{
	public partial class App : Application
	{
		public override void Initialize()
		{
			AvaloniaXamlLoader.Load(this);
		}

		public App()
		{
			ShowWindowCommand = ReactiveCommand.Create(ShowWindow);
			PauseMonitoringCommand = ReactiveCommand.Create(PauseMonitoring);
			ResumeMonitoring_ProcessBufferedEvents_Command = ReactiveCommand.Create(() => ResumeMonitoring(true));
			ResumeMonitoring_DiscardBufferedEvents_Command = ReactiveCommand.Create(() => ResumeMonitoring(false));
			ExitCommand = ReactiveCommand.Create(Exit);

			DataContext = this;

			_notifications = new FreeDesktopNotifications();
			_notifications.Connect();

			BeginConnectToBackupService();

			MainWindow = default!;
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
				desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

			base.OnFrameworkInitializationCompleted();

			lock (_queuedNotificationsSync)
			{
				var mainWindow = new MainWindow();

				MainWindow = mainWindow;

				foreach (var notification in _queuedNotifications)
					AddNotificationToMainWindow(notification);

				_queuedNotifications.Clear();
			}
		}

		public ICommand ShowWindowCommand { get; }
		public ICommand PauseMonitoringCommand { get; }
		public ICommand ResumeMonitoring_ProcessBufferedEvents_Command { get; }
		public ICommand ResumeMonitoring_DiscardBufferedEvents_Command { get; }
		public ICommand ExitCommand { get; }

		public MainWindow MainWindow { get; set; }

		object _queuedNotificationsSync = new object();
		List<Notification> _queuedNotifications = new List<Notification>();

		bool _shuttingDown;
		BridgeClient? _bridgeClient;
		INotifications? _notifications;

		void ShowWindow()
		{
			MainWindow ??= new MainWindow();

			if (MainWindow.IsVisible)
			{
				MainWindow.Activate();

				// Activate doesn't seem to always (or ever?) work with KDE Plasma. Maybe it's
				// trying to be helpful by preventing applications from forcing their way to
				// attention. In any event, this is the workaround I have identified at this
				// point: Make the window Topmost until the user activates it, at which point
				// it is returned to normal.
				MainWindow.Topmost = true;
			}
			else
				MainWindow.Show();
		}

		void PauseMonitoring()
		{
			using (var client = BridgeClient.ConnectTo(Path.Combine(OperatingParameters.DefaultIPCPath, BridgeServer.UNIXSocketName)))
			{
				var request = new BridgeMessage_PauseMonitor_Request();

				client.SendRequestAndReceiveResponse(request);
			}
		}

		void ResumeMonitoring(bool processBufferedPaths)
		{
			using (var client = BridgeClient.ConnectTo(Path.Combine(OperatingParameters.DefaultIPCPath, BridgeServer.UNIXSocketName)))
			{
				var request = new BridgeMessage_UnpauseMonitor_Request();

				request.ProcessBufferedPaths = processBufferedPaths;

				client.SendRequestAndReceiveResponse(request);
			}
		}

		void Exit()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				_shuttingDown = true;

				_bridgeClient?.Dispose();
				_bridgeClient = null;

				desktop.Shutdown();
			}
		}

		void BeginConnectToBackupService()
		{
			Task.Run(
				() =>
				{
					while (true)
					{
						try
						{
							_bridgeClient = BridgeClient.ConnectTo(
								Path.Combine(OperatingParameters.DefaultIPCPath, BridgeServer.UNIXSocketName));

							new Thread(NotificationsThreadProc) { IsBackground = true }.Start(_bridgeClient);

							break;
						}
						catch
						{
							Thread.Sleep(TimeSpan.FromSeconds(5));
						}
					}
				});
		}

		void NotificationsThreadProc(object? state)
		{
			var bridgeClient = (BridgeClient)state!;

			long lastMessageID = long.MinValue;

			while (ReferenceEquals(bridgeClient, _bridgeClient))
			{
				try
				{
					var request = new BridgeMessage_ReceiveNotifications_Request();

					request.LastMessageID = lastMessageID;
					request.Timeout = TimeSpan.FromSeconds(30);

					var response = bridgeClient.SendRequestAndReceiveResponse(request);

					if (response is BridgeMessage_ReceiveNotifications_Response notificationsResponse)
					{
						var messages = notificationsResponse.Messages;

						if (messages != null)
						{
							lock (_queuedNotificationsSync)
							{
								foreach (var notification in messages)
								{
									ShowNotificationToast(notification);

									if (MainWindow != null)
										AddNotificationToMainWindow(notification);
									else
										_queuedNotifications.Add(notification);

									if (notification.MessageID > lastMessageID)
										lastMessageID = notification.MessageID;
								}
							}
						}
					}
				}
				catch
				{
					bridgeClient.Dispose();
					break;
				}
			}

			if (_shuttingDown)
				Console.WriteLine("Notifications pump thread exiting");
			else
			{
				Console.WriteLine("Notifications pump thread will restart in 5 seconds");

				Thread.Sleep(TimeSpan.FromSeconds(5));

				BeginConnectToBackupService();
			}
		}

		void AddNotificationToMainWindow(Notification notification)
		{
			if (MainWindow is MainWindow mainWindow)
			{
				switch (notification.Event)
				{
					case StateEvent.RescanStarted:
						mainWindow.NotifyRescanStarted();
						break;
					case StateEvent.RescanCompleted:
						mainWindow.NotifyRescanCompleted();
						break;
				}

				mainWindow.AddNotification(notification);
			}
		}

		void ShowNotificationToast(Notification notification)
		{
			var notifications = _notifications;

			if (notifications != null)
				notifications.PostNotification(notification.ErrorMessage ?? "", notification.Summary ?? EventText.GetEventText(notification.Event), notification.Error);
		}
	}
}

