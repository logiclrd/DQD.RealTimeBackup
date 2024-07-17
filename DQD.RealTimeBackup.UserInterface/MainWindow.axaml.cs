using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.Bridge;
using DQD.RealTimeBackup.Bridge.Messages;
using DQD.RealTimeBackup.Bridge.Notifications;
using DQD.RealTimeBackup.Scan;
using DQD.RealTimeBackup.UserInterface.Controls;

using Timer = System.Timers.Timer;

namespace DQD.RealTimeBackup.UserInterface
{
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();

			_refreshTimer = new Timer();
			_refreshTimer.Interval = TimeSpan.FromSeconds(2).TotalMilliseconds;
			_refreshTimer.Elapsed += refreshTimer_Elapsed;

			this.Loaded += MainWindow_Loaded;
			this.Activated += MainWindow_Activated;

			var app = (App)Application.Current!;

			app.MainWindow = this;
		}

		Timer _refreshTimer;
		BridgeClient? _bridgeClient;
		bool _isConnected;
		bool _gatherRescanStatus;

		void MainWindow_Loaded(object? sender, RoutedEventArgs e)
		{
			CenterWindow();

			slcStatistics.ConfigureForGetStatsResponse();
			slcRescan.ConfigureForRescanStatus(includeBackupAgentQueueSizes: false);

			BeginConnectToBackupService();
		}

		void MainWindow_Activated(object? sender, EventArgs e)
		{
			// As a work-around for Window.Activate doing nothing, clicking the tray icon
			// sets the window to be Topmost. Activating the window then clears this and
			// returns things to normal.
			if (Topmost)
				Topmost = false;
		}

		void tbNotifications_IsCheckedChanged(object? sender, RoutedEventArgs e)
		{
			var statsRow = grdRoot.RowDefinitions[1];
			var notificationsRow = grdRoot.RowDefinitions[2];

			Height -= svNotifications.Bounds.Height;

			if (tbNotifications.IsChecked ?? false)
			{
				statsRow.Height = GridLength.Auto;
				notificationsRow.Height = GridLength.Star;
			}
			else
			{
				statsRow.Height = GridLength.Star;
				notificationsRow.Height = new GridLength(0);
			}

			UpdateLayout();
			Dispatcher.UIThread.Post(() => { });

			Height += svNotifications.Bounds.Height;
		}

		public static readonly StyledProperty<bool> IncludePeriodicRescansInNotificationListProperty =
			AvaloniaProperty.Register<NotificationView, bool>(nameof(IncludePeriodicRescansInNotificationList));

		public bool IncludePeriodicRescansInNotificationList
		{
			get => GetValue(IncludePeriodicRescansInNotificationListProperty);
			set => SetValue(IncludePeriodicRescansInNotificationListProperty, value);
		}

		static MainWindow()
		{
			IncludePeriodicRescansInNotificationListProperty.Changed.AddClassHandler<MainWindow>(MainWindow_IncludePeriodicRescansInNotificationListChanged);
		}

		static void MainWindow_IncludePeriodicRescansInNotificationListChanged(MainWindow sender, AvaloniaPropertyChangedEventArgs e)
		{
			bool shouldShowRescans = (bool?)e.NewValue ?? false;
			sender.NotifyIncludePeriodicRescansInNotificationListChanged(shouldShowRescans);
		}

		void NotifyIncludePeriodicRescansInNotificationListChanged(bool shouldShowRescans)
		{
			if (shouldShowRescans)
			{
				bool isAtBottom = (svNotifications.Offset.Y > svNotifications.ScrollBarMaximum.Y - 10);

				spNotifications.Classes.Remove("HideRescan");

				if (isAtBottom)
					svNotifications.ScrollToEnd();
			}
			else
				spNotifications.Classes.Add("HideRescan");
		}

		public void AddNotification(Notification notification)
		{
			if (!Dispatcher.UIThread.CheckAccess())
			{
				Dispatcher.UIThread.Invoke(
					() =>
					{
						AddNotification(notification);
					});

				return;
			}

			var view = new NotificationView();

			if ((notification.Event == StateEvent.RescanStarted)
			 || (notification.Event == StateEvent.RescanCompleted))
				view.Classes.Add("Rescan");

			spNotifications.Children.Add(view);

			view[!NotificationView.IncludePeriodicRescansInListProperty] = this[!IncludePeriodicRescansInNotificationListProperty];

			view.ToggleIncludePeriodicRescansInList +=
				(_, _) =>
				{
					IncludePeriodicRescansInNotificationList = !IncludePeriodicRescansInNotificationList;
				};

			view.SetNotificationContent(notification);

			view.CopyToClipboard +=
				(sender, text) =>
				{
					if (Clipboard != null)
						Clipboard.SetTextAsync(text).Wait();
				};

			lblNoNotifications.IsVisible = false;
			cmdClearNotifications.IsVisible = true;
		}

		void cmdClearNotifications_Click(object? sender, RoutedEventArgs e)
		{
			lblNoNotifications.IsVisible = true;
			spNotifications.Children.Clear();
			cmdClearNotifications.IsVisible = false;
		}

		void cmdPerformRescan_Click(object? sender, RoutedEventArgs e)
		{
			using (var client = BridgeClient.ConnectTo(Path.Combine(OperatingParameters.DefaultIPCPath, BridgeServer.UNIXSocketName)))
			{
				var request = new BridgeMessage_PerformRescan_Request();

				client.SendRequestAndReceiveResponse(request);
			}
		}

		void cmdCancelRescan_Click(object? sender, RoutedEventArgs e)
		{
			using (var client = BridgeClient.ConnectTo(Path.Combine(OperatingParameters.DefaultIPCPath, BridgeServer.UNIXSocketName)))
			{
				var request = new BridgeMessage_CancelRescan_Request();

				client.SendRequestAndReceiveResponse(request);
			}
		}

		protected override void OnClosing(WindowClosingEventArgs e)
		{
			e.Cancel = true;
			Hide();
		}

		void CenterWindow()
		{
			// BUG: Avalonia UI doesn't seem to factor DesktopScaling into its automatic window centring function.
			if (Screens.Primary is Screen primaryScreen)
			{
				this.Position = new PixelPoint(
					primaryScreen.WorkingArea.X + (int)(primaryScreen.WorkingArea.Width - this.Width * this.DesktopScaling) / 2,
					primaryScreen.WorkingArea.Y + (int)(primaryScreen.WorkingArea.Height - this.Height * this.DesktopScaling) / 2);
			}
		}

		bool IsConnected
		{
			get => _isConnected;
			set
			{
				if (!Dispatcher.UIThread.CheckAccess())
				{
					Dispatcher.UIThread.Invoke(
						() =>
						{
							IsConnected = value;
						});

					return;
				}

				_isConnected = value;

				if (!_isConnected)
				{
					lblConnected.IsVisible = false;
					lblNotConnected.IsVisible = true;
				}
				else
				{
					lblNotConnected.IsVisible = false;
					lblConnected.IsVisible = true;
				}
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

							IsConnected = true;

							_refreshTimer.Enabled = true;

							break;
						}
						catch
						{
							Thread.Sleep(TimeSpan.FromSeconds(5));
						}
					}
				});
		}

		public void NotifyRescanStarted()
		{
			if (!Dispatcher.UIThread.CheckAccess())
			{
				Dispatcher.UIThread.Invoke(NotifyRescanStarted);
				return;
			}

			_gatherRescanStatus = true;
			cmdPerformRescan.IsVisible = false;
			cmdCancelRescan.IsVisible = true;
			grdRescan.IsVisible = true;
		}

		public void NotifyRescanCompleted()
		{
			if (!Dispatcher.UIThread.CheckAccess())
			{
				Dispatcher.UIThread.Invoke(NotifyRescanCompleted);
				return;
			}

			_gatherRescanStatus = false;
			grdRescan.IsVisible = false;
			cmdCancelRescan.IsVisible = false;
			cmdPerformRescan.IsVisible = true;
		}

		void refreshTimer_Elapsed(object? sender, EventArgs e)
		{
			try
			{
				_refreshTimer.Enabled = false;

				if (_bridgeClient == null)
				{
					IsConnected = false;
					_refreshTimer.Enabled = false;
					Thread.Sleep(TimeSpan.FromSeconds(5));
					BeginConnectToBackupService();
					return;
				}

				var statsRequest = new BridgeMessage_GetStats_Request();

				var statsResponse = _bridgeClient.SendRequestAndReceiveResponse(statsRequest);

				if (statsResponse.Error != null)
					throw new Exception(statsResponse.Error.Message);

				RescanStatus? rescanStatus = null;

				if (_gatherRescanStatus)
				{
					var rescanRequest = new BridgeMessage_GetRescanStatus_Request();

					rescanRequest.Wait = false;

					var response = _bridgeClient.SendRequestAndReceiveResponse(rescanRequest);

					if (response is BridgeMessage_GetRescanStatus_Response rescanResponse)
						rescanStatus = rescanResponse.RescanStatus;
				}

				Dispatcher.UIThread.Post(
					() =>
					{
						slcStatistics.StatisticsObject = statsResponse;

						if (rescanStatus != null)
						{
							slcRescan.StatisticsObject = rescanStatus;

							if (grdRescan.IsVisible == false)
								grdRescan.IsVisible = true;
						}

						if ((statsResponse is BridgeMessage_GetStats_Response getStatsResponse)
						 && (getStatsResponse.BackupAgentQueueSizes?.UploadThreads is UploadStatus?[] uploadStatuses))
						{
							while (spUploads.Children.Count > uploadStatuses.Length)
								spUploads.Children.RemoveAt(spUploads.Children.Count - 1);
							while (spUploads.Children.Count < uploadStatuses.Length)
							{
								var uploadStatusControl = new UploadStatusControl();

								uploadStatusControl.CopyPath += (_, path) => Clipboard?.SetTextAsync(path).Wait();

								spUploads.Children.Add(uploadStatusControl);
							}

							for (int i=0; i < uploadStatuses.Length; i++)
							{
								var uploadStatusControl = (UploadStatusControl)spUploads.Children[i];

								if (uploadStatuses[i] is UploadStatus uploadStatus)
								{
									uploadStatusControl.UploadStatus = uploadStatus;
									uploadStatusControl.Opacity = 1.0;
								}
								else if (uploadStatusControl.Opacity == 1.0)
									uploadStatusControl.Opacity = 0.4;
								else
									uploadStatusControl.Opacity = 0.0;
							}
						}
					});
			}
			catch (Exception ex)
			{
				Console.WriteLine("Refresh timer exception: {0}: {1}", ex.GetType().Name, ex.Message);

				IsConnected = false;

				_bridgeClient?.Dispose();
				_bridgeClient = null;

				_refreshTimer.Enabled = false;

				BeginConnectToBackupService();
			}
			finally
			{
				if (IsConnected)
					_refreshTimer.Enabled = true;
			}
		}
	}
}
