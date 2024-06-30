using System;
using System.Threading;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.Bridge;
using DQD.RealTimeBackup.Bridge.Messages;

using DQD.RealTimeBackup.Console.Formatters;

namespace DQD.RealTimeBackup.Console.Commands
{
	public class GetStats
	{
		public static void Execute(BridgeClient connection, IOutputFormatter output, bool includeUploads, bool repeat, CancellationToken cancellationToken)
		{
			var request = new BridgeMessage_GetStats_Request();

			bool needHeading = true;

			DateTime nextStatsQuery = DateTime.UtcNow;

			do
			{
				var delay = nextStatsQuery - DateTime.UtcNow;

				if (delay > TimeSpan.Zero)
				{
					WaitHandle.WaitAny(
						new[] { cancellationToken.WaitHandle },
						delay);

					if (cancellationToken.IsCancellationRequested)
						break;
				}

				var response = connection.SendRequestAndReceiveResponse(request); 

				if (response is BridgeMessage_GetStats_Response getStatsResponse)
				{
					if (needHeading)
					{
						output.EmitGetStatsHeading();
						needHeading = false;
					}

					output.EmitGetStatsResponse(getStatsResponse);

					if (includeUploads && (getStatsResponse.BackupAgentQueueSizes?.UploadThreads is UploadStatus?[] uploadThreads))
					{
						output.EmitUploadThreads(uploadThreads);
						needHeading = true;
					}
				}

				nextStatsQuery += TimeSpan.FromSeconds(1);
			} while (repeat);
		}
	}
}
