using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_CheckPath_Request : BridgeRequestMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.CheckPath_Request;

		public string? Path;

		protected override void SerializeImplementation(ByteBuffer buffer)
		{
			if (Path == null)
				buffer.AppendByte(0);
			else
			{
				buffer.AppendByte(1);
				buffer.AppendString(Path);
			}
		}

		protected override void DeserializeImplementation(ByteBuffer buffer)
		{
			byte havePath = buffer.ReadByte();

			if (havePath != 0)
				Path = buffer.ReadString();
			else
				Path = null;
		}
	}
}
