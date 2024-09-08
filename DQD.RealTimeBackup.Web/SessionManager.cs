using System;
using System.Collections.Generic;
using System.Linq;

namespace DQD.RealTimeBackup.Web
{
	public class SessionManager : ISessionManager
	{
		Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
		object _sync = new object();

		static readonly TimeSpan MaximumSessionInactivityTime = TimeSpan.FromHours(2);

		public Session? GetSession(string sessionID)
		{
			lock (_sync)
			{
				if (_sessions.TryGetValue(sessionID, out var session))
					session.LastActivityDateTimeUTC = DateTime.UtcNow;

				return session;
			}
		}

		public Session StartSession()
		{
			string sessionID = Guid.NewGuid().ToString("N");

			var newSession = new Session(sessionID);

			lock (_sync)
				_sessions[sessionID] = newSession;

			return newSession;
		}

		public void EndSession(string sessionID)
		{
			lock (_sync)
				_sessions.Remove(sessionID);
		}

		public void CullSessions()
		{
			lock (_sync)
			{
				foreach (var sessionID in _sessions.Keys.ToList())
				{
					var session = _sessions[sessionID];

					var inactivityDuration = DateTime.UtcNow - session.LastActivityDateTimeUTC;

					if (inactivityDuration > MaximumSessionInactivityTime)
						_sessions.Remove(sessionID);
				}
			}
		}
	}
}
