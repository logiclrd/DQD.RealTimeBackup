using System;
using System.Collections.Generic;

namespace DeltaQ.RTB.Interop
{
	public class FileAccessNotifyEvent
	{
		public FileAccessNotifyEventMetadata Metadata { get; set; }
		public List<FileAccessNotifyEventInfo> InformationStructures { get; } = new List<FileAccessNotifyEventInfo>();
	}
}

