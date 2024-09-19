using System.Text;

using DQD.RealTimeBackup.Agent;

namespace DQD.RealTimeBackup.Scan
{
	public class ScanStatusFormatter
	{
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

		public string Headings => FormatLine(
			HeadingLabels[0],
			HeadingLabels[1],
			HeadingLabels[2],
			HeadingLabels[3],
			HeadingLabels[4],
			HeadingLabels[5],
			HeadingLabels[6],
			HeadingLabels[7],
			HeadingLabels[8]);

		public string Separator
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

		public string ToString(ScanStatus status)
		{
			return FormatLine(
				status.MountsProcessed,
				status.FilesDiscovered,
				status.DirectoryQueueSize,
				status.BackupAgentQueueSizes?.NumberOfFilesPendingIntake,
				status.BackupAgentQueueSizes?.NumberOfFilesPollingOpenHandles,
				status.BackupAgentQueueSizes?.NumberOfFilesPollingContentChanges,
				status.BackupAgentQueueSizes?.NumberOfBackupQueueActions,
				status.BackupAgentQueueSizes?.NumberOfQueuedUploads,
				status.ZFSSnapshotCount);
		}

		public string ToString(UploadStatus? status, int maxChars, bool useANSIProgressBar)
		{
			if ((status == null) || (status.Progress == null))
				return "";

			if (maxChars < 0)
				maxChars = 120;

			string speed;

			if (status.Progress.BytesPerSecond > 800000)
				speed = (status.Progress.BytesPerSecond / 1048576.0).ToString("#,##0.0") + " mb/s";
			else if (status.Progress.BytesPerSecond > 1024)
				speed = (status.Progress.BytesPerSecond / 1024.0).ToString("#,##0.0") + " kb/s";
			else
				speed = status.Progress.BytesPerSecond.ToString("#,##0") + "    b/s";

			speed = speed.PadLeft(12);

			double byteScale;
			string byteFormat;
			string byteUnit;

			if (status.Progress.TotalBytes < 1024)
			{
				byteScale = 1.0;
				byteFormat = "  #,##0";
				byteUnit = "  b";
			}
			else if (status.Progress.TotalBytes < 1024 * 1024)
			{
				byteScale = 1024.0;
				byteFormat = "#,##0.0";
				byteUnit = " kb";
			}
			else if (status.Progress.TotalBytes < 800 * 1024 * 1024)
			{
				byteScale = 1024 * 1024.0;
				byteFormat = "#,##0.0";
				byteUnit = " mb";
			}
			else if (status.Progress.TotalBytes < 800 * 1024 * 1024 * 1024L)
			{
				byteScale = 1024 * 1024 * 1024.0;
				byteFormat = "#,##0.0";
				byteUnit = " gb";
			}
			else
			{
				byteScale = 1024 * 1024 * 1024 * 1024.0;
				byteFormat = "#,##0.0";
				byteUnit = " tb";
			}

			string bytesTransferred = (status.Progress.BytesTransferred / byteScale).ToString(byteFormat).PadLeft(byteFormat.Length);
			string fileSize = (status.Progress.TotalBytes / byteScale).ToString(byteFormat).PadLeft(byteFormat.Length);

			string bytes = bytesTransferred + "/" + fileSize + byteUnit;

			const string TurnOnReverse = "\x1B[7m";
			const string ResetAttributes = "\x1B[0m";

			if (useANSIProgressBar)
			{
				speed = ResetAttributes + speed;

				int charactersOfBytesRepresentingCompletedTransfer = (int)(bytes.Length * status.Progress.BytesTransferred / status.FileSize);

				if (charactersOfBytesRepresentingCompletedTransfer > 0)
				{
					if (charactersOfBytesRepresentingCompletedTransfer > bytes.Length)
						charactersOfBytesRepresentingCompletedTransfer = bytes.Length;

					bytes =
						TurnOnReverse + bytes.Substring(0, charactersOfBytesRepresentingCompletedTransfer) +
						ResetAttributes + bytes.Substring(charactersOfBytesRepresentingCompletedTransfer);
				}
			}

			string formattedLine = speed + " [" + bytes + "] " + status.Path;

			if (formattedLine.Length > maxChars)
			{
				formattedLine = formattedLine.Substring(0, maxChars);

				// If we've chopped an ANSI escape sequence in half, remove the broken sequence and
				// replace it with a Reset Attributes sequence.
				int escapeStart = formattedLine.LastIndexOf("\x1B[");

				if (escapeStart > 0)
				{
					int escapeEnd = formattedLine.IndexOf('m', escapeStart);

					if (escapeEnd < 0)
						formattedLine = formattedLine.Substring(0, escapeStart) + ResetAttributes;
				}
			}

			return formattedLine;
		}
	}
}
