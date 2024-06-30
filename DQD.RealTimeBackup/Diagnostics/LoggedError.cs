namespace DQD.RealTimeBackup.Diagnostics
{
	public class LoggedError : ILoggedError
	{
		string _serializedErrorText;

		public LoggedError(string serializedErrorText)
		{
			_serializedErrorText = serializedErrorText;
		}

		public LoggedErrorException ToException() => new LoggedErrorException(_serializedErrorText);
	}
}
