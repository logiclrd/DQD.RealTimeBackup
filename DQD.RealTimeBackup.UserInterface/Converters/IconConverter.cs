using System;
using System.Globalization;

using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DQD.RealTimeBackup.UserInterface.Converters
{
	public class IconConverter : IValueConverter
	{
		WindowIcon _disconnected;
		WindowIcon _connected;

		public IconConverter()
		{
			_disconnected = new WindowIcon(AssetLoader.Open(new Uri("avares://DQD.RealTimeBackup.UserInterface/DQD.RealTimeBackup-Disconnected.ico")));
			_connected = new WindowIcon(AssetLoader.Open(new Uri("avares://DQD.RealTimeBackup.UserInterface/DQD.RealTimeBackup.ico")));
		}

		public object ProvideValue()
		{
			return this;
		}

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if ((value is bool connected) && connected)
				return _connected;
			else
				return _disconnected;
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			return null;
		}
	}
}
