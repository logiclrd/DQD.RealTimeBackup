using DQD.RealTimeBackup.Bridge.Notifications;
using DQD.RealTimeBackup.Bridge.Serialization;

namespace DQD.RealTimeBackup.Bridge.Messages
{
	public class BridgeMessage_ReceiveNotifications_Response : BridgeResponseMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.ReceiveNotifications_Response;

		[FieldOrder(0)]
		public Notification[]? Messages;
	}
}
