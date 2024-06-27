using DeltaQ.RTB.Bridge.Serialization;
using DeltaQ.RTB.Scan;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_GetRescanStatus_Response : BridgeResponseMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.GetRescanStatus_Response;

		[FieldOrder(1)]
		public RescanStatus? RescanStatus;
	}
}
