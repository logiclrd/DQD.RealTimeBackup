using DQD.RealTimeBackup.Bridge.Serialization;
using DQD.RealTimeBackup.Scan;

namespace DQD.RealTimeBackup.Bridge.Messages
{
	public class BridgeMessage_PerformRescan_Response : BridgeResponseMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.PerformRescan_Response;

		[FieldOrder(0)]
		public int RescanNumber;
		[FieldOrder(1)]
		public bool AlreadyRunning;

		public PerformRescanResponse ToPerformRescanResponse()
		{
			return
				new PerformRescanResponse()
				{
					RescanNumber = RescanNumber,
					AlreadyRunning = AlreadyRunning,
				};
		}
	}
}
