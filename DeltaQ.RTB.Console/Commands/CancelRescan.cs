using DeltaQ.RTB.Bridge;
using DeltaQ.RTB.Bridge.Messages;

using DeltaQ.RTB.Console.Formatters;

namespace DeltaQ.RTB.Console.Commands
{
	public class CancelRescan
	{
		public static void Execute(BridgeClient connection, IOutputFormatter output)
		{
			var request = new BridgeMessage_CancelRescan_Request();

			var response = connection.SendRequestAndReceiveResponse(request); 

			if (response is BridgeMessage_CancelRescan_Response)
				output.EmitRescanCancelled();
		}
	}
}
