using System;
using System.Runtime.InteropServices;

namespace DeltaQ.RTB.Interop
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct FileAccessNotifyEventMetadata
	{
		public byte Version;
		public byte Reserved;
		public short MetadataLength;
		public FileAccessNotifyEventMask Mask;
		public int FileDescriptor;
		public int ProcessID;
	}
}

