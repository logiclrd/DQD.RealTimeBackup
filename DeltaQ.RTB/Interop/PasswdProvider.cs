using System;
using System.IO;

namespace DeltaQ.RTB.Interop
{
	public class PasswdProvider : IPasswdProvider
	{
		public TextReader OpenRead()
		{
			return new StreamReader("/etc/passwd");
		}
	}
}
