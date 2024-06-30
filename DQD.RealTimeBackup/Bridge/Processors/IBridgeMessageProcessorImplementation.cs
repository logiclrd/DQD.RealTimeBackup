using DQD.RealTimeBackup.Bridge.Messages;

namespace DQD.RealTimeBackup.Bridge.Processors
{
	public interface IBridgeMessageProcessorImplementation
	{
		BridgeMessageType MessageType { get; }
		bool IsLongRunning { get; }
		ProcessMessageResult? ProcessMessage(BridgeRequestMessage message);
	}
}
