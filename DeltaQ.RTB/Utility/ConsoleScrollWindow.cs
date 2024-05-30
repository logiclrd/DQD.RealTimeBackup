using System;
using System.Security.Principal;

namespace DeltaQ.RTB.Utility
{
	public class ConsoleScrollWindow : IDisposable
	{
		int _firstRow;
		int _lastRow;

		public int FirstRow
		{
			get => _firstRow;
			set
			{
				_firstRow = value;
				if (_isEnabled)
					TurnOn();
			}
		}

		public int LastRow
		{
			get => _lastRow;
			set
			{
				_lastRow = value;
				if (_isEnabled)
					TurnOn();
			}
		}

		bool _isEnabled = false;
		bool _disposed = false;

		public bool IsEnabled => _isEnabled;

		public ConsoleScrollWindow(int firstRow, int lastRow, bool initiallyEnabled = true)
		{
			_firstRow = firstRow;
			_lastRow = lastRow;

			if (initiallyEnabled)
				TurnOn();
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				TurnOff();
				_disposed = true;
			}
		}

		public void TurnOn()
		{
			if (!_disposed)
			{
				// The magic sauce: DECSTBM -- DEC Set Top and Bottom Margins
				if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
					Console.Write("\x1B[{0};{1}r", _firstRow, _lastRow);

				_isEnabled = true;
			}
		}

		public void TurnOff()
		{
			if (!_disposed)
				if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
					Console.Write("\x1B[;r");

			_isEnabled = false;
		}

		class DisableScope : IDisposable
		{
			ConsoleScrollWindow? _window;
			bool _wasEnabled;
			(int X, int Y) _savedCursorPosition;

			public DisableScope(ConsoleScrollWindow window)
			{
				_wasEnabled = window.IsEnabled && !Console.IsOutputRedirected;

				_window = window;
				if (_wasEnabled)
				{
					_savedCursorPosition = Console.GetCursorPosition();
					_window?.TurnOff();
				}
			}

			public void Dispose()
			{
				if (_wasEnabled)
				{
					if (_window != null)
					{
						_window.TurnOn();

						if (_savedCursorPosition.Y < _window._firstRow - 1)
							_savedCursorPosition.Y = _window.FirstRow - 1;
						if (_savedCursorPosition.Y > _window._lastRow - 1)
							_savedCursorPosition.Y = _window._lastRow - 1;
					
						Console.SetCursorPosition(_savedCursorPosition.X, _savedCursorPosition.Y);
					}
				}

				_window = null;
			}
		}

		public IDisposable Suspend() => new DisableScope(this);
	}
}
