using DeltaQ.RTB.Agent;
using DeltaQ.RTB.Bridge.Serialization;
using DeltaQ.RTB.Storage;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_GetStats_Response : BridgeResponseMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.GetStats_Response;

		[FieldOrder(0)]
		public BackupAgentQueueSizes? BackupAgentQueueSizes;
		[FieldOrder(1)]
		public int ZFSSnapshotCount;

		// TODO: rescan in progress?
	}
}
