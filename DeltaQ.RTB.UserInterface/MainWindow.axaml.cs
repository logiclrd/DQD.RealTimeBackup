using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.Bridge;
using DeltaQ.RTB.Bridge.Messages;
using DeltaQ.RTB.Scan;
using DeltaQ.RTB.UserInterface.Controls;

using Timer = System.Timers.Timer;

namespace DeltaQ.RTB.UserInterface
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
					rConnectedStatus.Text = "not connected";
					tbConnectedStatus.Foreground = Brushes.Black;
				}
				else
				{
					rConnectedStatus.Text = "connected";
					tbConnectedStatus.Foreground = Brushes.Green;
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
			grdRescan.IsVisible = true;
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
			catch
			{
				IsConnected = false;

				_bridgeClient?.Dispose();
				_bridgeClient = null;

				_refreshTimer.Enabled = false;

				BeginConnectToBackupService();
			}
			finally
			{
				_refreshTimer.Enabled = true;
			}
		}
	}
}
