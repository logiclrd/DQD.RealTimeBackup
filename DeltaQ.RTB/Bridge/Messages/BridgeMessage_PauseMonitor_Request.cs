using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_PauseMonitor_Request : BridgeRequestMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.PauseMonitor_Request;

		protected override void SerializeImplementation(ByteBuffer buffer) { }
		protected override void DeserializeImplementation(ByteBuffer buffer) { }
	}
}
