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

		public event EventHandler? Resized;

		void lblTimestamp_PointerPressed(object? sender, PointerPressedEventArgs e)
		{
			_useUTC = !_useUTC;
			RenderTimestamp();
		}

		bool _resizing;
		double _resizeStartY;
		double _resizeStartHeight;
		double _maximumHeight;

		void spResize_PointerPressed(object? sender, PointerPressedEventArgs e)
		{
			e.Pointer.Capture(spResize);
			_resizing = true;
			_resizeStartY = e.GetPosition(this).Y;
			_resizeStartHeight = this.MaxHeight;

			stbNotification.Measure(new Size(stbNotification.Bounds.Width, double.MaxValue));

			_maximumHeight = stbNotification.Bounds.Top + stbNotification.DesiredSize.Height;
		}

		void spResize_PointerMoved(object? sender, PointerEventArgs e)
		{
			if (_resizing)
			{
				double delta = e.GetPosition(this).Y - _resizeStartY;

				this.MaxHeight = Math.Min(_resizeStartHeight + delta, _maximumHeight);
			}
		}

		void spResize_PointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			e.Pointer.Capture(null);
			_resizing = false;

			Resized?.Invoke(this, EventArgs.Empty);
		}

		void spResize_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
		{
			_resizing = false;
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

