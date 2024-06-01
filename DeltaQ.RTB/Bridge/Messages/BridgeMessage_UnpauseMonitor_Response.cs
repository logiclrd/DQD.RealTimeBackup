using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_UnpauseMonitor_Response : BridgeResponseMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.UnpauseMonitor_Response;
	}
}
