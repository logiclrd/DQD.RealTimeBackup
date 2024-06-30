using System;
using System.Globalization;

using Avalonia.Data.Converters;

namespace DQD.RealTimeBackup.UserInterface.Converters
{
	public class StringIsNotEmptyConverter : IValueConverter
	{
		public object ProvideValue()
		{
			return this;
		}

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if ((value is string str) && !string.IsNullOrWhiteSpace(str))
				return true;

			return false;
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			return null;
		}
	}
}
