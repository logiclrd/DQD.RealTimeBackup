namespace DQD.RealTimeBackup.Web
{
	public interface ISessionManager
	{
		Session? GetSession(string sessionID);
		Session StartSession();
		void EndSession(string sessionID);
		void CullSessions();
	}
}
