using System;
using System.IO;
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

			BeginConnectToBackupService();
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
				desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

			base.OnFrameworkInitializationCompleted();

			ShowWindow();
		}

		public ICommand ShowWindowCommand { get; }
		public ICommand PauseMonitoringCommand { get; }
		public ICommand ResumeMonitoring_ProcessBufferedEvents_Command { get; }
		public ICommand ResumeMonitoring_DiscardBufferedEvents_Command { get; }
		public ICommand ExitCommand { get; }

		public MainWindow? MainWindow { get; set; }

		bool _shuttingDown;
		BridgeClient? _bridgeClient;

		void ShowWindow()
		{
			MainWindow ??= new MainWindow();
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

							new Thread(NotificationsThreadProc).Start(_bridgeClient);

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
							foreach (var notification in messages)
							{
								switch (notification.Event)
								{
									case StateEvent.RescanStarted:
										MainWindow?.NotifyRescanStarted();
										break;
									case StateEvent.RescanCompleted:
										MainWindow?.NotifyRescanCompleted();
										break;
								}

								ShowNotificationToast(notification);

								if (notification.MessageID > lastMessageID)
									lastMessageID = notification.MessageID;
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

			if (!_shuttingDown)
			{
				Thread.Sleep(TimeSpan.FromSeconds(5));

				BeginConnectToBackupService();
			}
		}

		void ShowNotificationToast(Notification notification)
		{
			// TODO
		}
	}
}

