using DQD.RealTimeBackup.Bridge;
using DQD.RealTimeBackup.Bridge.Messages;

using DQD.RealTimeBackup.Console.Formatters;

namespace DQD.RealTimeBackup.Console.Commands
{
	public class UnpauseMonitor
	{
		public static void Execute(BridgeClient connection, IOutputFormatter output, bool discardBufferedNotifications)
		{
			var request = new BridgeMessage_UnpauseMonitor_Request();

			request.ProcessBufferedPaths = !discardBufferedNotifications;

			var response = connection.SendRequestAndReceiveResponse(request); 

			if (response is BridgeMessage_UnpauseMonitor_Response)
				output.EmitMonitorUnpaused();
		}
	}
}
