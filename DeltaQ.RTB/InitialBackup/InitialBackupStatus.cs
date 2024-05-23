using System;
using System.IO;
using System.Text;
using DeltaQ.RTB.Agent;

namespace DeltaQ.RTB.InitialBackup
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

		static string FormatLine(
			object mountsProcessed, object filesDiscovered, object directoryQueueSize,
			object? filesPendingIntake, object? filesPollingOpenHandles, object? filesPollingContentChanges, object? backupQueueActions, object? queuedUploads,
			object zfsSnapshotCount)
		{
			return
				FormatItem(mountsProcessed, 16) + "|" +
				FormatItem(filesDiscovered, 16) + "|" +
				FormatItem(directoryQueueSize, 20) + "|" +
				FormatItem(filesPendingIntake, 20) + "|" +
				FormatItem(filesPollingOpenHandles, 21) + "|" +
				FormatItem(filesPollingContentChanges, 21) + "|" +
				FormatItem(backupQueueActions, 17) + "|" +
				FormatItem(queuedUploads, 14) + "|" +
				FormatItem(zfsSnapshotCount, 13);
		}

		public static string Headings => FormatLine(
			"Mounts Processed",
			"Files Discovered",
			"Directory Queue Size",
			"Files Pending Intake",
			"Files w/ Open Handles",
			"Files Polling Content",
			"Backup Queue Size",
			"Queued Uploads",
			"ZFS Snapshots");

		public static string Separator
		{
			get
			{
				var builder = new StringBuilder(Headings);

				for (int i=0; i < builder.Length; i++)
					if (builder[i] == '|')
						builder[i] = '+';
					else
						builder[i] = ' ';

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
