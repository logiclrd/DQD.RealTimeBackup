using DQD.RealTimeBackup.Bridge;
using DQD.RealTimeBackup.Bridge.Messages;

using DQD.RealTimeBackup.Console.Formatters;

namespace DQD.RealTimeBackup.Console.Commands
{
	public class FollowRescan
	{
		public static void Execute(BridgeClient connection, IOutputFormatter output, int rescanNumber = -1)
		{
			bool haveHeadings = false;

			while (true)
			{
				var request = new BridgeMessage_GetRescanStatus_Request();

				request.Wait = true;

				var response = connection.SendRequestAndReceiveResponse(request); 

				if (response is BridgeMessage_GetRescanStatus_Response getRescanStatusResponse)
				{
					if (getRescanStatusResponse.RescanStatus == null)
						output.EmitNoRescanStatus();
					else
					{
						if (rescanNumber == -1)
							rescanNumber = getRescanStatusResponse.RescanStatus.RescanNumber;
						else if (rescanNumber != getRescanStatusResponse.RescanStatus.RescanNumber)
							break;
						else
						{
							if (!haveHeadings)
							{
								output.EmitRescanStatusHeadings();
								haveHeadings = true;
							}
							
							output.EmitRescanStatus(getRescanStatusResponse.RescanStatus);

							if (!getRescanStatusResponse.RescanStatus.IsRunning)
								break;
						}
					}
				}
			}
		}
	}
}
