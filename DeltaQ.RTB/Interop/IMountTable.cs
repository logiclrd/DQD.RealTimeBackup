using System;
using System.Collections.Generic;

namespace DeltaQ.RTB.Interop
{
	public interface IMountTable
	{
		IMountHandle OpenMountForFileSystem(string mountPointPath);
		IEnumerable<IMount> EnumerateMounts();
	}
}

