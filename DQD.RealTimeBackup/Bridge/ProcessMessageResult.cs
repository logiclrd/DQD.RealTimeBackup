using DQD.RealTimeBackup.Bridge.Messages;

namespace DQD.RealTimeBackup.Bridge
{
	public class ProcessMessageResult
	{
		public BridgeMessage? ResponseMessage = null;
		public bool DisconnectClient = false;

		public static ProcessMessageResult Message(BridgeResponseMessage responseMessage) => new ProcessMessageResult() { ResponseMessage = responseMessage };
	}
}
