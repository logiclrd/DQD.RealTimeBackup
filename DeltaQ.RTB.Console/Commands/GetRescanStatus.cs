using DeltaQ.RTB.Bridge;
using DeltaQ.RTB.Bridge.Messages;

using DeltaQ.RTB.Console.Formatters;

namespace DeltaQ.RTB.Console.Commands
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
