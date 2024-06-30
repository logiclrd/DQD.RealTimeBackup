using DQD.RealTimeBackup.Bridge.Serialization;
using DQD.RealTimeBackup.Scan;

namespace DQD.RealTimeBackup.Bridge.Messages
{
	public class BridgeMessage_GetRescanStatus_Response : BridgeResponseMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.GetRescanStatus_Response;

		[FieldOrder(1)]
		public RescanStatus? RescanStatus;
	}
}
