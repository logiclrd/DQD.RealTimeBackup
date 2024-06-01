using DeltaQ.RTB.Bridge;
using DeltaQ.RTB.Bridge.Messages;

using DeltaQ.RTB.Console.Formatters;

namespace DeltaQ.RTB.Console.Commands
{
	public class PerformRescan
	{
		public static void Execute(BridgeClient connection, IOutputFormatter output, out int rescanNumber)
		{
			rescanNumber = -1;

			var request = new BridgeMessage_PerformRescan_Request();

			var response = connection.SendRequestAndReceiveResponse(request); 

			if (response is BridgeMessage_PerformRescan_Response performRescanResponse)
			{
				rescanNumber = performRescanResponse.RescanNumber;

				output.EmitRescanStarted(performRescanResponse.ToPerformRescanResponse());
			}
		}
	}
}
