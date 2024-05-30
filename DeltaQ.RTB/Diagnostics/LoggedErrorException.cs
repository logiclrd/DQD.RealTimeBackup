using System;

namespace DeltaQ.RTB.Diagnostics
{
	public class LoggedErrorException : Exception
	{
		public LoggedErrorException(string message)
			: base(message)
		{
		}
	}
}
