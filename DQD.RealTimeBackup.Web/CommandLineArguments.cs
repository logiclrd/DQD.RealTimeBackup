using DQD.CommandLineParser;

namespace DQD.RealTimeBackup.Web
{
	public class CommandLineArguments
	{
		[Switch("/DAEMON", Description =
			"Runs DQD.RealTimeBackup.Web as a daemon (detached child process).")]
		public bool Daemon;

		[Argument("/GETPASSWORDHASH", Description =
			"Gets the password hash to be placed into configuration to allow the specified password to be used to access DQD.RealTimeBackup.Web.")]
		public string? GetPasswordHash = default;

		[Switch("/?")]
		public bool ShowUsage;
	}
}
