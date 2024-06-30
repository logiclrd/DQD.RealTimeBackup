using DQD.RealTimeBackup.Bridge;
using DQD.RealTimeBackup.Bridge.Messages;

using DQD.RealTimeBackup.Console.Formatters;

namespace DQD.RealTimeBackup.Console.Commands
{
	public class GetRescanStatus
	{
		public static void Execute(BridgeClient connection, IOutputFormatter output)
		{
			var request = new BridgeMessage_GetRescanStatus_Request();

			var response = connection.SendRequestAndReceiveResponse(request); 

			if (response is BridgeMessage_GetRescanStatus_Response getRescanStatusResponse)
			{
				if (getRescanStatusResponse.RescanStatus == null)
					output.EmitNoRescanStatus();
				else
					output.EmitRescanStatus(getRescanStatusResponse.RescanStatus);
			}
		}
	}
}
