using System;
using System.Text;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.Bridge.Messages;
using DQD.RealTimeBackup.Scan;

using Konsole = System.Console;

namespace DQD.RealTimeBackup.Console.Formatters
{
	public class TextOutputFormatter : IOutputFormatter
	{
		ScanStatusFormatter _scanStatusFormatter;

		public TextOutputFormatter(ScanStatusFormatter scanStatusFormatter)
		{
			_scanStatusFormatter = scanStatusFormatter;
		}

		public void EmitGetStatsHeading()
		{
			Konsole.WriteLine("DQD.RealTimeBackup STATISTICS");
			Konsole.WriteLine("=============================");
			Konsole.WriteLine();
			Konsole.WriteLine("Timestamp: {0:yyyy-MM-dd HH:mm:ss.fffffff}", DateTime.Now);
			Konsole.WriteLine();
			Konsole.WriteLine(Headings);
			Konsole.WriteLine(Separator);
		}

		static string[] HeadingLabels =
			new[]
			{
				"Files Pending Intake",
				"Open Handles Check",
				"Files Polling Content",
				"Backup Queue Size",
				"Queued Uploads",
				"ZFS Snapshots",
			};

		public static string Headings => FormatLine(
			HeadingLabels[0],
			HeadingLabels[1],
			HeadingLabels[2],
			HeadingLabels[3],
			HeadingLabels[4],
			HeadingLabels[5]);

		public static string Separator
		{
			get
			{
				var builder = new StringBuilder(Headings);

				for (int i=0; i < builder.Length; i++)
					if (builder[i] == '|')
						builder[i] = '+';
					else
						builder[i] = '-';

				return builder.ToString();
			}
		}

		static string FormatItem(object? item, int fieldSize)
		{
			if (item is int number)
			{
				string formatted = number.ToString("#,##0");

				return ' ' + formatted.PadLeft(fieldSize) + ' ';
			}
			else if (item != null)
				return ' ' + item.ToString()!.PadRight(fieldSize) + ' ';
			else
				return new string(' ', fieldSize + 2);
		}

		static string FormatLine(
			object? filesPendingIntake, object? filesPollingOpenHandles, object? filesPollingContentChanges, object? backupQueueActions, object? queuedUploads,
			object zfsSnapshotCount)
		{
			return
				FormatItem(filesPendingIntake, HeadingLabels[0].Length) + "|" +
				FormatItem(filesPollingOpenHandles, HeadingLabels[1].Length) + "|" +
				FormatItem(filesPollingContentChanges, HeadingLabels[2].Length) + "|" +
				FormatItem(backupQueueActions, HeadingLabels[3].Length) + "|" +
				FormatItem(queuedUploads, HeadingLabels[4].Length) + "|" +
				FormatItem(zfsSnapshotCount, HeadingLabels[5].Length);
		}

		public void EmitGetStatsResponse(BridgeMessage_GetStats_Response message)
		{
			Konsole.WriteLine(FormatLine(
				message.BackupAgentQueueSizes?.NumberOfFilesPendingIntake,
				message.BackupAgentQueueSizes?.NumberOfFilesPollingOpenHandles,
				message.BackupAgentQueueSizes?.NumberOfFilesPollingContentChanges,
				message.BackupAgentQueueSizes?.NumberOfBackupQueueActions,
				message.BackupAgentQueueSizes?.NumberOfQueuedUploads,
				message.ZFSSnapshotCount));
		}

		public void EmitUploadThreads(UploadStatus?[] uploadThreads)
		{
			Konsole.WriteLine();
			Konsole.WriteLine("Upload threads:");

			for (int i=0; i < uploadThreads.Length; i++)
			{
				if (uploadThreads[i] is UploadStatus uploadThreadStatus)
					Konsole.WriteLine("[{0}] {1}", i, _scanStatusFormatter.ToString(uploadThreadStatus, int.MaxValue));
				else
					Konsole.WriteLine("[{0}] idle", i);
			}
		}

		public void EmitPathSubmittedForCheck(string path)
		{
			Konsole.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fffffff}] Path submitted: {1}", DateTime.Now, path);
		}

		public void EmitMonitorPaused()
		{
			Konsole.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fffffff}] *** Monitor Paused", DateTime.Now);
		}

		public void EmitMonitorUnpaused()
		{
			Konsole.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fffffff}] *** Monitor Unpaused", DateTime.Now);
		}

		public void EmitRescanStarted(PerformRescanResponse response)
		{
			Konsole.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fffffff}] A rescan has been requested", DateTime.Now);
			Konsole.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fffffff}] => Rescan number: {1}", DateTime.Now, response.RescanNumber);
			if (response.AlreadyRunning)
				Konsole.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fffffff}] => This rescan was already running", DateTime.Now);
			else
				Konsole.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fffffff}] => This rescan was just started in response to this request", DateTime.Now);
		}

		public void EmitNoRescanStatus()
		{
			Konsole.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fffffff}] No rescan status information is available", DateTime.Now);
		}

		public void EmitRescanStatusHeadings()
		{
			Konsole.WriteLine(_scanStatusFormatter.Headings);
			Konsole.WriteLine(_scanStatusFormatter.Separator);
		}

		public void EmitRescanStatus(RescanStatus status)
		{
			Konsole.WriteLine(_scanStatusFormatter.ToString(status));
		}

		public void EmitRescanCancelled()
		{
			Konsole.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fffffff}] Cancellation has been requested for any rescan in progress", DateTime.Now);
		}

		public void EmitError(Exception ex)
		{
			Konsole.WriteLine("----------------");
			Konsole.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fffffff}] ERROR", DateTime.Now);
			Konsole.WriteLine();
			Konsole.WriteLine(ex);
		}

		public void Close()
		{
		}
	}
}
