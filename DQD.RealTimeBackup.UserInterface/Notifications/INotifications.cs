using DQD.RealTimeBackup.Bridge.Messages;

namespace DQD.RealTimeBackup.UserInterface.Notifications
{
	public interface INotifications
	{
		void Connect();
		void PostNotification(string notificationText, string? summaryText, ErrorInfo? errorInfo);
	}
}
