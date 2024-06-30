using System;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.Bridge.Messages;
using DQD.RealTimeBackup.Scan;

namespace DQD.RealTimeBackup.Console.Formatters
{
	public interface IOutputFormatter
	{
		void EmitGetStatsHeading();
		void EmitGetStatsResponse(BridgeMessage_GetStats_Response message);
		void EmitUploadThreads(UploadStatus?[] uploadThreads);
		void EmitPathSubmittedForCheck(string path);
		void EmitMonitorPaused();
		void EmitMonitorUnpaused();
		void EmitRescanStarted(PerformRescanResponse response);
		void EmitNoRescanStatus();
		void EmitRescanStatusHeadings();
		void EmitRescanStatus(RescanStatus status);
		void EmitRescanCancelled();

		void EmitError(Exception ex);

		void Close();
	}
}
