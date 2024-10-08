using DQD.RealTimeBackup.Bridge.Serialization;

namespace DQD.RealTimeBackup.Bridge.Messages
{
	public class BridgeMessage_CancelUpload_Request : BridgeRequestMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.CancelUpload_Request;

		[FieldOrder(0)]
		public int UploadThreadIndex;
	}
}
