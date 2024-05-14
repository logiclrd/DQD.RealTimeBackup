using System;
using System.Collections.Generic;

namespace DeltaQ.RTB
{
	public interface IOpenFileHandles
	{
		IEnumerable<OpenFileHandle> Enumerate(string path);
	}
}

