using DQD.RealTimeBackup.Bridge;
using DQD.RealTimeBackup.Bridge.Messages;

using DQD.RealTimeBackup.Console.Formatters;

namespace DQD.RealTimeBackup.Console.Commands
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
