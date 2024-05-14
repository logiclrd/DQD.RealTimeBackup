using System;

namespace DeltaQ.RTB
{
	public interface IMountHandle
	{
		int FileDescriptor { get; }
		long FileSystemID { get; }
	}
}
