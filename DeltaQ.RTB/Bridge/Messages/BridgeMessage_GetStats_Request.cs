using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_GetStats_Request : BridgeRequestMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.GetStats_Request;
	}
}
