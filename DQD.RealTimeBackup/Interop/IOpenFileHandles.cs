using System;
using System.Collections.Generic;

namespace DQD.RealTimeBackup.Interop
{
	public interface IOpenFileHandles
	{
		IEnumerable<OpenFileHandle> EnumerateAll();
		IEnumerable<OpenFileHandle> Enumerate(string path);
	}
}

