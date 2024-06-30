using System;
using System.Globalization;

using Avalonia.Data.Converters;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.Storage;

namespace DQD.RealTimeBackup.UserInterface.Converters
{
	public class UploadStatusProgressConverter : IValueConverter
	{
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is UploadStatus uploadStatus)
				if (uploadStatus.Progress is UploadProgress uploadProgress)
					return uploadProgress.BytesTransferred / (double)uploadStatus.FileSize;

			return 0.0;
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			return null;
		}
	}
}
