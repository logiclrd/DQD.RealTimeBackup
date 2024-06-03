using System;
using System.Globalization;

using Avalonia.Data.Converters;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.Storage;

namespace DeltaQ.RTB.UserInterface.Converters
{
	public class UploadStatusSpeedConverter : IValueConverter
	{
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is UploadStatus uploadStatus)
			{
				if (uploadStatus.Progress is UploadProgress uploadProgress)
				{
					var bytesPerSecond = uploadProgress.BytesPerSecond;

					if (bytesPerSecond > 800000 * 1024)
						return (bytesPerSecond / 1_073_741_824.0).ToString("#,##0") + " gb/s";
					else if (bytesPerSecond > 800000)
						return (bytesPerSecond / 1048576.0).ToString("#,##0.0") + " mb/s";
					else if (bytesPerSecond > 1024)
						return (bytesPerSecond / 1024.0).ToString("#,##0.0") + " kb/s";
					else
						return bytesPerSecond.ToString("#,##0") + " b/s";
				}
			}

			return "";
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			return null;
		}
	}
}
