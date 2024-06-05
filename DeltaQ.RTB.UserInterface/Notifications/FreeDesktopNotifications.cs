using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

using DeltaQ.RTB.Bridge.Messages;

using Tmds.DBus;

namespace DeltaQ.RTB.UserInterface.Notifications
{
	public class FreeDesktopNotifications : INotifications, IDisposable
	{
		public static readonly ObjectPath ObjectPath = new ObjectPath("/org/freedesktop/Notifications");

		object _sync = new object();
		Connection? _dbusConnection;
		IFreeDesktopNotifications? _proxy;
		uint _lastNotificationID;
		int _notificationCount;

		public void Connect()
		{
			_dbusConnection = Connection.Session;

			Wait(_dbusConnection.ConnectAsync());

			_proxy = _dbusConnection.CreateProxy<IFreeDesktopNotifications>(
				"org.freedesktop.Notifications",
				ObjectPath);

			_proxy.WatchNotificationClosedAsync(FreeDesktopNotifications_NotificationClosed, FreeDesktopNotifications_NotificationError);
		}

		void FreeDesktopNotifications_NotificationClosed((uint id, uint reason) args)
		{
			_notificationCount = 0;
			_lastNotificationID = 0;
		}

		void FreeDesktopNotifications_NotificationError(Exception exception)
		{
		}

		public void Dispose()
		{
			_dbusConnection?.Dispose();
			_dbusConnection = null;
		}

		public void PostNotification(string notificationText, string? summaryText, ErrorInfo? errorInfo)
		{
			lock (_sync)
			{
				_notificationCount++;

				string title = summaryText ?? "Important Backup Notification";

				if (_notificationCount > 1)
				{
					if (_notificationCount == 2)
						title += " (one other)";
					else
						title += " (" + _notificationCount + " others)";
				}

				string body = notificationText;

				if (errorInfo != null)
				{
					string exception = FormatException(errorInfo);

					body += exception;
				}

				if (_proxy != null)
				{
					_lastNotificationID = Wait(_proxy.NotifyAsync(
						"DeltaQ.RTB",
						_lastNotificationID,
						GetPathToIcon(),
						title,
						body,
						Array.Empty<string>(),
						new Dictionary<string, object>()
						{
							{ "urgency", (byte)1 },
							{ "category", "transfer" },
						},
						(int)TimeSpan.FromSeconds(8).TotalMilliseconds));
				}
			}
		}

		string FormatException(ErrorInfo errorInfo)
		{
			var builder = new StringBuilder();

			FormatException(errorInfo, builder);

			return builder.ToString();
		}

		void FormatException(ErrorInfo errorInfo, StringBuilder builder)
		{
			builder.Append("<p>");
			builder.Append("<b>").Append(HttpUtility.HtmlEncode(errorInfo.ExceptionType!.Split('.').Last())).Append("</b>");
			builder.Append(": ").Append(errorInfo.Message);
			builder.Append("</p>");

			if ((errorInfo.InnerError != null) || ((errorInfo.InnerErrors != null) && (errorInfo.InnerErrors.Count > 0)))
			{
				builder.Append("<ul>");

				if (errorInfo.InnerError != null)
				{
					builder.Append("<li>");
					FormatException(errorInfo.InnerError, builder);
					builder.Append("</li>");
				}

				if (errorInfo.InnerErrors != null)
				{
					foreach (var innerError in errorInfo.InnerErrors)
					{
						if (innerError != null)
						{
							builder.Append("<li>");
							FormatException(innerError, builder);
							builder.Append("</li>");
						}
					}
				}

				builder.Append("</ul>");
			}

			builder.Append("</div>");
		}

		string GetPathToIcon()
		{
			string binaryFilePath = typeof(FreeDesktopNotifications).Assembly.Location;

			string binaryDirectoryPath = Path.GetDirectoryName(binaryFilePath)!;

			return Path.Combine(binaryDirectoryPath, "Icon.png");
		}

		void Wait(Task task)
		{
			task.ConfigureAwait(false);
			task.Wait();
		}

		TResult Wait<TResult>(Task<TResult> task)
		{
			task.ConfigureAwait(false);
			task.Wait();

			return task.Result;
		}
	}
}
