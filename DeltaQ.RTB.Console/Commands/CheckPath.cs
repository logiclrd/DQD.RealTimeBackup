using DeltaQ.RTB.Bridge;
using DeltaQ.RTB.Bridge.Messages;

using DeltaQ.RTB.Console.Formatters;

namespace DeltaQ.RTB.Console.Commands
{
	public class CheckPath
	{
		public static void Execute(BridgeClient connection, IOutputFormatter output, string path)
		{
			var request = new BridgeMessage_CheckPath_Request();

			request.Path = path;

			var response = connection.SendRequestAndReceiveResponse(request); 

			if (response is BridgeMessage_CheckPath_Response)
				output.EmitPathSubmittedForCheck(path);
		}
	}
}
