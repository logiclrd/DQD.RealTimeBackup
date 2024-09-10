using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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

		public void StartSessionExpiryThread()
		{
			var thread = new Thread(SessionExpiryThread);

			thread.IsBackground = true;
			thread.Start();
		}

		void SessionExpiryThread()
		{
			try
			{
				while (true)
				{
					Thread.Sleep(TimeSpan.FromSeconds(30));
					CullSessions();
				}
			}
			catch (Exception e)
			{
				Console.WriteLine("Crash on session expiry thread:");
				Console.WriteLine(e);

				Thread.Sleep(TimeSpan.FromSeconds(10));

				Console.WriteLine("Restarting session expiry thread");

				StartSessionExpiryThread();
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
