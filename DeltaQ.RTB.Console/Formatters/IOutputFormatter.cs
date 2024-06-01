using System;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.Bridge.Messages;

namespace DeltaQ.RTB.Console.Formatters
{
	public interface IOutputFormatter
	{
		void EmitGetStatsHeading();
		void EmitGetStatsResponse(BridgeMessage_GetStats_Response message);
		void EmitUploadThreads(UploadStatus?[] uploadThreads);
		void EmitPathSubmittedForCheck(string path);
		void EmitMonitorPaused();
		void EmitMonitorUnpaused();

		void EmitError(Exception ex);

		void Close();
	}
}
