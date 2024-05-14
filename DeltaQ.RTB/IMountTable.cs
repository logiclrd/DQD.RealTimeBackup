using System;
using System.Collections.Generic;

namespace DeltaQ.RTB
{
	public interface IMountTable
	{
		IMountHandle OpenMountForFileSystem(string mountPointPath);
		IEnumerable<IMount> EnumerateMounts();
	}
}

