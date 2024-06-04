using DeltaQ.RTB.Bridge.Notifications;
using DeltaQ.RTB.Bridge.Serialization;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_ReceiveNotifications_Response : BridgeResponseMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.ReceiveNotifications_Response;

		[FieldOrder(0)]
		public Notification[]? Messages;
	}
}
