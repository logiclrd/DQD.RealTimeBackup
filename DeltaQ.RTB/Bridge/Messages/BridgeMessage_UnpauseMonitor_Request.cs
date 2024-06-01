using DeltaQ.RTB.Bridge.Serialization;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_UnpauseMonitor_Request : BridgeRequestMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.UnpauseMonitor_Request;

		[FieldOrder(0)]
		public bool ProcessBufferedPaths;
	}
}
