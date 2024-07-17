using System;
using System.Runtime.Serialization;

namespace DQD.RealTimeBackup.Storage
{
	public class NullResponseException : Exception
	{
		public NullResponseException()
		{
		}

		public NullResponseException(string message)
			: base(message)
		{
		}
	}
}
