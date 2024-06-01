using DeltaQ.RTB.Bridge.Messages;

namespace DeltaQ.RTB.Bridge.Processors
{
	public interface IBridgeMessageProcessor
	{
		ProcessMessageResult? ProcessMessage(BridgeMessage message);
	}
}
