using System;
using System.Globalization;

using Avalonia.Data.Converters;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.Storage;

namespace DQD.RealTimeBackup.UserInterface.Converters
{
	public class UploadStatusProgressTextConverter : IValueConverter
	{
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is UploadStatus uploadStatus)
			{
				if (uploadStatus.Progress is UploadProgress uploadProgress)
				{
					long fileSize = uploadStatus.FileSize;
					long bytesTransferred = uploadProgress.BytesTransferred;

					double byteScale;
					string byteFormat;
					string byteUnit;

					if (fileSize < 1024)
					{
						byteScale = 1.0;
						byteFormat = "#,##0";
						byteUnit = " b";
					}
					else if (fileSize < 1024 * 1024)
					{
						byteScale = 1024.0;
						byteFormat = "#,##0.0";
						byteUnit = " kb";
					}
					else if (fileSize < 800 * 1024 * 1024)
					{
						byteScale = 1024 * 1024.0;
						byteFormat = "#,##0.0";
						byteUnit = " mb";
					}
					else if (fileSize < 800 * 1024 * 1024 * 1024L)
					{
						byteScale = 1024 * 1024 * 1024.0;
						byteFormat = "#,##0.0";
						byteUnit = " gb";
					}
					else
					{
						byteScale = 1024 * 1024 * 1024 * 1024.0;
						byteFormat = "#,##0.0";
						byteUnit = " tb";
					}

					string bytesTransferredFormatted = (bytesTransferred / byteScale).ToString(byteFormat);
					string fileSizeFormatted = (fileSize / byteScale).ToString(byteFormat).PadLeft(byteFormat.Length);

					return bytesTransferredFormatted + " / " + fileSizeFormatted + byteUnit;
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
