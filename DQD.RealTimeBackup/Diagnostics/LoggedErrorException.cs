using System;

namespace DQD.RealTimeBackup.Diagnostics
{
	public class LoggedErrorException : Exception
	{
		public LoggedErrorException(string message)
			: base(message)
		{
		}
	}
}
