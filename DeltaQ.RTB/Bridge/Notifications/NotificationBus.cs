using System;
using System.Collections.Generic;
using System.Threading;

namespace DeltaQ.RTB.Bridge.Notifications
{
	public class NotificationBus : INotificationBus
	{
		const int BufferLength = 1000;

		object _sync = new object();
		Queue<Notification> _buffer = new Queue<Notification>();

		public void Post(Notification notification)
		{
			lock (_sync)
			{
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
				var deadlineUTC = DateTime.UtcNow + timeout;

				while (DateTime.UtcNow < deadlineUTC)
				{
					List<Notification>? ret = null;

					foreach (var message in _buffer)
					{
						if (message.MessageID > lastMessageID)
						{
							ret ??= new List<Notification>();
							ret.Add(message);
						}
					}

					if ((ret != null) && (ret.Count > 0))
						return ret.ToArray();

					var remainingTime = deadlineUTC - DateTime.UtcNow;

					if (remainingTime > TimeSpan.Zero)
						Monitor.Wait(_sync, remainingTime);
				}

				return null;
			}
		}
	}
}
