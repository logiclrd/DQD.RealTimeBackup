using DeltaQ.RTB.Bridge.Messages;

namespace DeltaQ.RTB.UserInterface.Notifications
{
	public interface INotifications
	{
		void Connect();
		void PostNotification(string notificationText, string? summaryText, ErrorInfo? errorInfo);
	}
}
