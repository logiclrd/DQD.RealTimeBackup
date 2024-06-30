using System;
using System.Collections.Generic;

namespace DQD.RealTimeBackup.Interop
{
	public interface IMountTable
	{
		IMountHandle OpenMountForFileSystem(string mountPointPath);
		IEnumerable<IMount> EnumerateMounts();
	}
}

