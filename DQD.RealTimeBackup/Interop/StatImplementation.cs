using System;

namespace DQD.RealTimeBackup.Interop
{
	public class StatImplementation : IStat
	{
		public StatInfo Stat(string path)
		{
			int result = NativeMethods.stat(path, out var statbuf);

			if (result != 0)
				throw new Exception("[" + result + "] Unable to stat path: " + path);

			return statbuf;
		}
	}
}
