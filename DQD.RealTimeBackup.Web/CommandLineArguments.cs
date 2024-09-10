using DQD.CommandLineParser;

namespace DQD.RealTimeBackup.Web
{
	public class CommandLineArguments
	{
		[Switch("/DAEMON")]
		public bool Daemon;
	}
}
