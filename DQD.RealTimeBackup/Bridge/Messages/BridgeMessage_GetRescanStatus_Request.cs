using DQD.RealTimeBackup.Bridge.Serialization;

namespace DQD.RealTimeBackup.Bridge.Messages
{
	public class BridgeMessage_GetRescanStatus_Request : BridgeRequestMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.GetRescanStatus_Request;

		[FieldOrder(0)]
		public bool Wait;
	}
}
