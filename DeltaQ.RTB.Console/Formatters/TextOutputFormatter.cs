using System;
using System.Text;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.Bridge.Messages;

using Konsole = System.Console;

namespace DeltaQ.RTB.Console.Formatters
{
	public class TextOutputFormatter : IOutputFormatter
	{
		public void EmitGetStatsHeading()
		{
			Konsole.WriteLine("DELTAQ.RTB STATISTICS");
			Konsole.WriteLine("=====================");
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
					Konsole.WriteLine("[{0}] {1}", i, uploadThreadStatus.Format(int.MaxValue));
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
