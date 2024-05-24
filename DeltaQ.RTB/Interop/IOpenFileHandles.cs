using System;
using System.Collections.Generic;

namespace DeltaQ.RTB.Interop
{
	public interface IOpenFileHandles
	{
		IEnumerable<OpenFileHandle> EnumerateAll();
		IEnumerable<OpenFileHandle> Enumerate(string path);
	}
}

