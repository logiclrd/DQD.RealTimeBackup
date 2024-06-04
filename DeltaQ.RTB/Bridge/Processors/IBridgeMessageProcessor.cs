using DeltaQ.RTB.Bridge.Messages;

namespace DeltaQ.RTB.Bridge.Processors
{
	public interface IBridgeMessageProcessor
	{
		bool IsLongRunning(BridgeRequestMessage message);

		ProcessMessageResult? ProcessMessage(BridgeRequestMessage message);
	}
}
