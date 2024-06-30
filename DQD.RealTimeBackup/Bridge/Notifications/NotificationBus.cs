using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace DQD.RealTimeBackup.Bridge.Notifications
{
	public class NotificationBus : INotificationBus
	{
		const int BufferLength = 1000;

		object _sync = new object();
		Queue<Notification> _buffer = new Queue<Notification>();
		long _nextMessageID = 0;

		object _ds = new object();

		void DebugLog(string message)
		{
			lock (_ds)
			{
				using (var writer = new StreamWriter("/tmp/DQD.RealTimeBackup.notifications.debuglog", append: true))
					writer.WriteLine(message);
			}
		}

		public void Post(Notification notification)
		{
			lock (_sync)
			{
				notification.MessageID = Interlocked.Increment(ref _nextMessageID);

				DebugLog("Post: " + notification.Event + ", assigning message ID " + notification.MessageID);

				_buffer.Enqueue(notification);

				while (_buffer.Count > BufferLength)
					_buffer.Dequeue();

				Monitor.PulseAll(_sync);
			}
		}

		public Notification[]? Receive(long lastMessageID, TimeSpan timeout)
		{
			lock (_sync)
			{
				try
				{
					DebugLog("Receive: last message id " + lastMessageID);

					var deadlineUTC = DateTime.UtcNow + timeout;

					DebugLog("Receive: set deadline to " + deadlineUTC);

					while (DateTime.UtcNow < deadlineUTC)
					{
						List<Notification>? ret = null;

						DebugLog("Receive: scanning for messages");

						foreach (var message in _buffer)
						{
							if (message.MessageID > lastMessageID)
							{
								ret ??= new List<Notification>();
								ret.Add(message);
							}
						}

						if ((ret != null) && (ret.Count > 0))
						{
							DebugLog("Receive: found " + ret.Count + " messages, returning");
							return ret.ToArray();
						}

						DebugLog("Receive: found no messages");

						var remainingTime = deadlineUTC - DateTime.UtcNow;

						DebugLog("Receive: no messages found, remaining time is " + remainingTime);

						if (remainingTime > TimeSpan.Zero)
						{
							DebugLog("Receive: sleeping");
							Monitor.Wait(_sync, remainingTime);
							DebugLog("Receive: woke up");
						}
					}
				}
				catch (Exception e)
				{
					DebugLog("Receive: " + e.ToString());
				}

				DebugLog("Receive: returning null");
				return null;
			}
		}
	}
}
