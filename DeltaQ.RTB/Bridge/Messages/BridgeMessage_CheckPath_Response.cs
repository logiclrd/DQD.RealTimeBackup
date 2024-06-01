using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_CheckPath_Response : BridgeResponseMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.CheckPath_Response;

		protected override void SerializeResponseImplementation(ByteBuffer buffer)
		{
		}

		protected override void DeserializeResponseImplementation(ByteBuffer buffer)
		{
		}
	}
}
