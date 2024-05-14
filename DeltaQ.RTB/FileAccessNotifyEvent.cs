using System;

namespace DeltaQ.RTB
{
	public class FileAccessNotifyEvent
	{
		public FileAccessNotifyEventMetadata Metadata { get; set; }
		public IntPtr AdditionalData { get; set; }
		public int AdditionalDataLength { get; set; }
	}
}

