using DeltaQ.RTB.Bridge;
using DeltaQ.RTB.Bridge.Messages;

using DeltaQ.RTB.Console.Formatters;

namespace DeltaQ.RTB.Console.Commands
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
