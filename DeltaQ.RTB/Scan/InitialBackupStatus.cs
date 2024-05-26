using System;
using System.IO;
using System.Linq;
using System.Text;
using DeltaQ.RTB.Agent;

namespace DeltaQ.RTB.Scan
{
	public class InitialBackupStatus
	{
		public int MountsProcessed;
		public int FilesDiscovered;
		public int DirectoryQueueSize;
		public BackupAgentQueueSizes? BackupAgentQueueSizes;
		public int ZFSSnapshotCount;

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

		static string[] HeadingLabels =
			new[]
			{
				"Mounts Processed",
				"Files Discovered",
				"Directory Queue Size",
				"Files Pending Intake",
				"Open Handles Check",
				"Files Polling Content",
				"Backup Queue Size",
				"Queued Uploads",
				"ZFS Snapshots",
			};

		static string FormatLine(
			object mountsProcessed, object filesDiscovered, object directoryQueueSize,
			object? filesPendingIntake, object? filesPollingOpenHandles, object? filesPollingContentChanges, object? backupQueueActions, object? queuedUploads,
			object zfsSnapshotCount)
		{
			return
				FormatItem(mountsProcessed, HeadingLabels[0].Length) + "|" +
				FormatItem(filesDiscovered, HeadingLabels[1].Length) + "|" +
				FormatItem(directoryQueueSize, HeadingLabels[2].Length) + "|" +
				FormatItem(filesPendingIntake, HeadingLabels[3].Length) + "|" +
				FormatItem(filesPollingOpenHandles, HeadingLabels[4].Length) + "|" +
				FormatItem(filesPollingContentChanges, HeadingLabels[5].Length) + "|" +
				FormatItem(backupQueueActions, HeadingLabels[6].Length) + "|" +
				FormatItem(queuedUploads, HeadingLabels[7].Length) + "|" +
				FormatItem(zfsSnapshotCount, HeadingLabels[8].Length);
		}

		public static string Headings => FormatLine(
			HeadingLabels[0],
			HeadingLabels[1],
			HeadingLabels[2],
			HeadingLabels[3],
			HeadingLabels[4],
			HeadingLabels[5],
			HeadingLabels[6],
			HeadingLabels[7],
			HeadingLabels[8]);

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

		public override string ToString()
		{
			return FormatLine(
				MountsProcessed,
				FilesDiscovered,
				DirectoryQueueSize,
				BackupAgentQueueSizes?.NumberOfFilesPendingIntake,
				BackupAgentQueueSizes?.NumberOfFilesPollingOpenHandles,
				BackupAgentQueueSizes?.NumberOfFilesPollingContentChanges,
				BackupAgentQueueSizes?.NumberOfBackupQueueActions,
				BackupAgentQueueSizes?.NumberOfQueuedUploads,
				ZFSSnapshotCount);
		}
	}
}
