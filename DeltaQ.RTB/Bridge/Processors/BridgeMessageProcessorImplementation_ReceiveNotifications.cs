using System;

using DeltaQ.RTB.Bridge.Messages;
using DeltaQ.RTB.Bridge.Notifications;

namespace DeltaQ.RTB.Bridge.Processors
{
	public class BridgeMessageProcessorImplementation_ReceiveNotifications : IBridgeMessageProcessorImplementation
	{
		public BridgeMessageType MessageType => BridgeMessageType.ReceiveNotifications_Request;

		INotificationBus _notificationBus;

		public BridgeMessageProcessorImplementation_ReceiveNotifications(INotificationBus notificationBus)
		{
			_notificationBus = notificationBus;
		}

		public ProcessMessageResult? ProcessMessage(BridgeMessage message)
		{
			var response = new BridgeMessage_ReceiveNotifications_Response();

			if (!(message is BridgeMessage_ReceiveNotifications_Request request))
			{
				response.Error = new ErrorInfo { Message = "Internal error: message passed to ProcessMessage is not of the right type" };
				return ProcessMessageResult.Message(response);
			}

			try
			{
				response.Messages = _notificationBus.Receive(request.LastMessageID, request.Timeout);
			}
			catch (Exception ex)
			{
				response.Error = new ErrorInfo(ex);
			}			

			return ProcessMessageResult.Message(response);
		}
	}
}
