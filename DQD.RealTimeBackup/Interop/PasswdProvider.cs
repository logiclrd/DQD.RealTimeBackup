using System;
using System.IO;

namespace DQD.RealTimeBackup.Interop
{
	public class PasswdProvider : IPasswdProvider
	{
		public TextReader OpenRead()
		{
			return new StreamReader("/etc/passwd");
		}
	}
}
