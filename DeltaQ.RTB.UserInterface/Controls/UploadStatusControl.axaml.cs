using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using DeltaQ.RTB.Agent;

namespace DeltaQ.RTB.UserInterface.Controls
{
	public partial class UploadStatusControl : UserControl
	{
		public UploadStatusControl()
		{
			InitializeComponent();

			DataContext = this;
		}

		public static readonly StyledProperty<UploadStatus> UploadStatusProperty =
			AvaloniaProperty.Register<UploadStatusControl, UploadStatus>(nameof(UploadStatus));

		public UploadStatus UploadStatus
		{
			get => GetValue(UploadStatusProperty);
			set => SetValue(UploadStatusProperty, value);
		}

		public event EventHandler<string>? CopyPath;

		void UploadStatus_PointerPressed(object sender, PointerPressedEventArgs e)
		{
			CopyPath?.Invoke(this, UploadStatus.Path);
		}
	}
}
