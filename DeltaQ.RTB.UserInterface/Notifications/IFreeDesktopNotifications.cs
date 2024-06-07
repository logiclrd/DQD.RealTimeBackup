using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Tmds.DBus;

namespace DeltaQ.RTB.UserInterface.Notifications
{
	/// <seealso>http://www.galago-project.org/specs/notification/0.9/x408.html</seealso>
	/// <summary>
	/// Interface for notifications
	/// </summary>
	[DBusInterface("org.freedesktop.Notifications")]
	public interface IFreeDesktopNotifications : IDBusObject
	{
		Task<uint> NotifyAsync(string appName, uint replacesID, string appIcon, string summary, string body, string[] actions, IDictionary<string, object> hints, int expireTimeoutMilliseconds);

		Task CloseNotificationAsync(uint id);

		Task<string[]> GetCapabilitiesAsync();

		Task<(string name, string vendor, string version, string spec_version)> GetServerInformationAsync();

		Task<IDisposable> WatchNotificationClosedAsync(Action<(uint id, uint reason)> handler, Action<Exception>? onError = null);

		Task<IDisposable> WatchActionInvokedAsync(Action<(uint id, string actionKey)> handler, Action<Exception>? onError = null);
	}
}
