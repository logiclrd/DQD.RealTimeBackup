using System.Runtime.InteropServices;

namespace DeltaQ.RTB
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct FileAccessNotifyEventMetadata
	{
		public byte Version;
		public byte Reserved;
		public short MetadataLength;
		public int _Alignment;
		public long Mask;
		public int FileDescriptor;
		public int ProcessID;
	}
}

