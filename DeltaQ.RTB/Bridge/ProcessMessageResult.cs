using DeltaQ.RTB.Bridge.Messages;

namespace DeltaQ.RTB.Bridge
{
	public class ProcessMessageResult
	{
		public BridgeMessage? ResponseMessage = null;
		public bool DisconnectClient = false;

		public static ProcessMessageResult Message(BridgeResponseMessage responseMessage) => new ProcessMessageResult() { ResponseMessage = responseMessage };
	}
}
