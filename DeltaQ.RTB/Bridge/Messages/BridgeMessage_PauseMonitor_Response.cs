using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_PauseMonitor_Response : BridgeResponseMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.PauseMonitor_Response;

		protected override void SerializeResponseImplementation(ByteBuffer buffer)
		{
		}

		protected override void DeserializeResponseImplementation(ByteBuffer buffer)
		{
		}
	}
}
