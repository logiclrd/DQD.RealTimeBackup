using System;

namespace DQD.RealTimeBackup.Bridge.Notifications
{
	public interface INotificationBus
	{
		void Post(Notification notification);
		Notification[]? Receive(long lastMessageID, TimeSpan timeout);
	}
}
