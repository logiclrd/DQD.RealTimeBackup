namespace DeltaQ.RTB.Diagnostics
{
	public interface ILoggedError
	{
		public LoggedErrorException ToException();
	}
}
