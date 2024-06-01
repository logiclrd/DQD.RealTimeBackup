using DeltaQ.RTB.Agent;
using DeltaQ.RTB.Bridge.Serialization;

namespace DeltaQ.RTB.Scan
{
	public class ScanStatus
	{
		[FieldOrder(0)]
		public int MountsProcessed;
		[FieldOrder(1)]
		public int FilesDiscovered;
		[FieldOrder(2)]
		public int DirectoryQueueSize;
		[FieldOrder(3)]
		public BackupAgentQueueSizes? BackupAgentQueueSizes;
		[FieldOrder(4)]
		public int ZFSSnapshotCount;
	}
}
