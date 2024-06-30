using DQD.RealTimeBackup.Bridge.Messages;

namespace DQD.RealTimeBackup.Bridge.Processors
{
	public interface IBridgeMessageProcessor
	{
		bool IsLongRunning(BridgeRequestMessage message);

		ProcessMessageResult? ProcessMessage(BridgeRequestMessage message);
	}
}
