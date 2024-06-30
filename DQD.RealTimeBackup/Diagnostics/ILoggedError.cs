namespace DQD.RealTimeBackup.Diagnostics
{
	public interface ILoggedError
	{
		public LoggedErrorException ToException();
	}
}
