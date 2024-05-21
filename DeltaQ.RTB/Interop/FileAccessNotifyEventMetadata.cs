using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

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

