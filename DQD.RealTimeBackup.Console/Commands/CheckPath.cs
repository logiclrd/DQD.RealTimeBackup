using DQD.RealTimeBackup.Bridge;
using DQD.RealTimeBackup.Bridge.Messages;

using DQD.RealTimeBackup.Console.Formatters;

namespace DQD.RealTimeBackup.Console.Commands
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
