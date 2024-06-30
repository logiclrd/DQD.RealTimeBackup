using DQD.RealTimeBackup.Bridge.Serialization;

namespace DQD.RealTimeBackup.Bridge.Messages
{
	public class BridgeMessage_CheckPath_Request : BridgeRequestMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.CheckPath_Request;

		[FieldOrder(0)]
		public string? Path;
	}
}
