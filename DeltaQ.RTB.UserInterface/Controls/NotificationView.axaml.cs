using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using DeltaQ.RTB.Bridge.Notifications;
using ReactiveUI;

namespace DeltaQ.RTB.UserInterface.Controls
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

		void lblTimestamp_PointerPressed(object? sender, PointerPressedEventArgs e)
		{
			_useUTC = !_useUTC;
			RenderTimestamp();
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
	}
}

