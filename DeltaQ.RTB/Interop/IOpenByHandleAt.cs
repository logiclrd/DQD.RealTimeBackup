using System;

namespace DeltaQ.RTB.Interop
{
	public interface IOpenByHandleAt
	{
		IOpenHandle? Open(int mountFileDescriptor, byte[] fileHandle);
	}
}

