using System;
using System.Globalization;

using Avalonia.Data.Converters;

using DeltaQ.RTB.Agent;

namespace DeltaQ.RTB.UserInterface.Converters
{
	public class UploadStatusPathConverter : IValueConverter
	{
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is UploadStatus uploadStatus)
				return uploadStatus.Path;

			return "";
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			return null;
		}
	}
}
