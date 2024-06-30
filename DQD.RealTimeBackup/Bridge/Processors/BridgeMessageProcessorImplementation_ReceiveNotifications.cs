using System;

using DQD.RealTimeBackup.Bridge.Messages;
using DQD.RealTimeBackup.Bridge.Notifications;

namespace DQD.RealTimeBackup.Bridge.Processors
{
	public class BridgeMessageProcessorImplementation_ReceiveNotifications : IBridgeMessageProcessorImplementation
	{
		public BridgeMessageType MessageType => BridgeMessageType.ReceiveNotifications_Request;

		INotificationBus _notificationBus;

		public BridgeMessageProcessorImplementation_ReceiveNotifications(INotificationBus notificationBus)
		{
			_notificationBus = notificationBus;
		}

		public bool IsLongRunning => true;

		public ProcessMessageResult? ProcessMessage(BridgeRequestMessage message)
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
