using System;

namespace DeltaQ.RTB.Interop
{
	public class FileAccessNotifyEvent
	{
		public FileAccessNotifyEventMetadata Metadata { get; set; }
		public IntPtr AdditionalData { get; set; }
		public int AdditionalDataLength { get; set; }
	}
}

