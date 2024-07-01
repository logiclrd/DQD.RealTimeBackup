using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

using DQD.RealTimeBackup.Bridge.Messages;
using DQD.RealTimeBackup.Bridge.Notifications;

namespace DQD.RealTimeBackup.UserInterface.Controls
{
	public partial class NotificationView : UserControl
	{
		public NotificationView()
		{
			InitializeComponent();

			DataContext = this;
		}

		public static readonly StyledProperty<string> TimestampFormattedProperty =
			AvaloniaProperty.Register<NotificationView, string>(nameof(TimestampFormatted));
		public static readonly StyledProperty<string> SummaryTextProperty =
			AvaloniaProperty.Register<NotificationView, string>(nameof(SummaryText));
		public static readonly StyledProperty<string> NotificationTextProperty =
			AvaloniaProperty.Register<NotificationView, string>(nameof(NotificationText));

		public event EventHandler<string>? CopyToClipboard;

		public string TimestampFormatted
		{
			get => GetValue(TimestampFormattedProperty);
			set => SetValue(TimestampFormattedProperty, value);
		}

		public string SummaryText
		{
			get => GetValue(SummaryTextProperty);
			set => SetValue(SummaryTextProperty, value);
		}

		public string NotificationText
		{
			get => GetValue(NotificationTextProperty);
			set => SetValue(NotificationTextProperty, value);
		}

		void mnuCopyNotification_Click(object? sender, RoutedEventArgs e)
		{
			CopyToClipboard?.Invoke(this, FormatNotification());
		}

		void mnuCopySelectedText_Click(object? sender, RoutedEventArgs e)
		{
			CopyToClipboard?.Invoke(this, stbNotification.SelectedText);
		}

		void stbNotification_ContextRequested(object? sender, RoutedEventArgs e)
		{
			mnuCopySelectedText.IsEnabled = (stbNotification.SelectionEnd > stbNotification.SelectionStart);
		}

		void lblTimestamp_PointerPressed(object? sender, PointerPressedEventArgs e)
		{
			var point = e.GetCurrentPoint(lblTimestamp);

			if (point.Properties.IsLeftButtonPressed)
			{
				_useUTC = !_useUTC;
				RenderTimestamp();
			}
		}

		Notification? _notification;
		bool _useUTC;

		public NotificationView SetNotificationContent(Notification notification)
		{
			_notification = notification;
			_useUTC = false;

			this.SummaryText = notification.Summary ?? EventText.GetEventText(notification.Event);
			this.NotificationText = notification.ErrorMessage ?? "";

			RenderTimestamp();

			var toolTipView = new TextBlock();

			toolTipView.FontSize = 12;
			toolTipView.FontFamily = "DejaVu Sans Mono";
			toolTipView.Width = 1600;
			toolTipView.Background = new SolidColorBrush(Color.FromRgb(246, 246, 185));
			toolTipView.Foreground = Brushes.Black;
			toolTipView.Text = FormatNotification();

			ToolTip.SetTip(this, toolTipView);

			return this;
		}

		void RenderTimestamp()
		{
			if (_notification == null)
				TimestampFormatted = "";
			else if (_useUTC)
				TimestampFormatted = _notification.TimestampUTC.ToString("yyyy-MM-dd HH:mm:ss") + " (UTC)";
			else
				TimestampFormatted = _notification.TimestampUTC.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
		}

		string FormatNotification()
		{
			var writer = new StringWriter();

			if (_notification != null)
			{
				writer.Write("Date/time: ");
				writer.WriteLine(TimestampFormatted);
				writer.WriteLine();

				if (_notification.Event != StateEvent.None)
				{
					switch (_notification.Event)
					{
						case StateEvent.InitialBackupStarted:   writer.WriteLine("Initial Backup Started"); break;
						case StateEvent.InitialBackupCompleted: writer.WriteLine("Initial Backup Completed"); break;
						case StateEvent.RescanStarted:          writer.WriteLine("Rescan Started"); break;
						case StateEvent.RescanCompleted:        writer.WriteLine("Rescan Completed"); break;
					}

					writer.WriteLine();
				}

				if (!string.IsNullOrWhiteSpace(_notification.Summary))
				{
					writer.WriteLine(_notification.Summary);
					writer.WriteLine();
				}

				if (!string.IsNullOrWhiteSpace(_notification.ErrorMessage))
				{
					writer.WriteLine(_notification.ErrorMessage);
					writer.WriteLine();
				}

				if (_notification.Error != null)
					FormatError(_notification.Error, "", writer);
			}

			return writer.ToString();
		}

		static void FormatError(ErrorInfo errorInfo, string indent, TextWriter writer)
		{
			writer.Write(indent);
			writer.Write(errorInfo.ExceptionType);
			writer.Write(": ");
			writer.WriteLine(errorInfo.Message);

			if (errorInfo.Source != null)
			{
				writer.Write(indent);
				writer.Write("Source: ");
				writer.WriteLine(errorInfo.Source);
			}

			if (errorInfo.StackTrace != null)
			{
				string[] lines = errorInfo.StackTrace.Split('\r', '\n');

				foreach (var line in lines)
				{
					writer.Write(indent);
					writer.WriteLine(line);
				}
			}

			if (errorInfo.InnerErrors != null)
			{
				for (int i=0; i < errorInfo.InnerErrors.Count; i++)
				{
					if (errorInfo.InnerErrors[i] is ErrorInfo innerError)
					{
						writer.WriteLine();
						writer.Write(indent);
						writer.WriteLine("Inner exception #{0}:", i + 1);

						FormatError(innerError, indent + "  ", writer);
					}
				}
			}
			else if (errorInfo.InnerError != null)
			{
				writer.WriteLine();
				writer.Write(indent);
				writer.WriteLine("Inner exception:");

				FormatError(errorInfo.InnerError, indent + "  ", writer);
			}
		}
	}
}

