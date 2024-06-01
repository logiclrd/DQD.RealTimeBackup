using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_UnpauseMonitor_Request : BridgeRequestMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.UnpauseMonitor_Request;

		public bool ProcessBufferedPaths;

		protected override void SerializeImplementation(ByteBuffer buffer)
		{
			buffer.AppendByte((byte)(ProcessBufferedPaths ? 1 : 0));
		}

		protected override void DeserializeImplementation(ByteBuffer buffer)
		{
			ProcessBufferedPaths = (buffer.ReadByte() != 0);
		}
	}
}
