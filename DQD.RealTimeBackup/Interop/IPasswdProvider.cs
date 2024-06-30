using System;
using System.IO;

namespace DQD.RealTimeBackup.Interop
{
	public interface IPasswdProvider
	{
		TextReader OpenRead();
	}
}
