using DeltaQ.RTB.Bridge.Messages;

namespace DeltaQ.RTB.Bridge.Processors
{
	public interface IBridgeMessageProcessorImplementation
	{
		BridgeMessageType MessageType { get; }
		ProcessMessageResult? ProcessMessage(BridgeMessage message);
	}
}
