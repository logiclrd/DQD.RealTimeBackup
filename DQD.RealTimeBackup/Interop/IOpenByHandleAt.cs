using System;

namespace DQD.RealTimeBackup.Interop
{
	public interface IOpenByHandleAt
	{
		IOpenHandle? Open(int mountFileDescriptor, byte[] fileHandle);
	}
}

