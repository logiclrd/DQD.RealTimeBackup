using System;
using System.Collections.Generic;
using System.Text;

using DeltaQ.RTB.Bridge.Notifications;

namespace DeltaQ.RTB.UserInterface
{
	public class EventText
	{
		static object s_eventTextSync = new object();
		static Dictionary<StateEvent, string> s_eventTextCache = new Dictionary<StateEvent, string>();

		public static string GetEventText(StateEvent notificationEvent)
		{
			lock (s_eventTextSync)
			{
				if (!s_eventTextCache.TryGetValue(notificationEvent, out var text))
				{
					text = s_eventTextCache[notificationEvent] = SpaceWords(notificationEvent.ToString());
				}

				return text ?? "(internal error: unknown notification event)";
			}
		}

		static string SpaceWords(string text)
		{
			var buffer = new StringBuilder();

			for (int i=0; i < text.Length; i++)
			{
				if ((i > 0) && char.IsUpper(text[i]))
					buffer.Append(' ');

				buffer.Append(text[i]);
			}

			return buffer.ToString();
		}
	}
}
