namespace DeltaQ.RTB.Agent
{
	public class BackupAgentQueueSizes
	{
		public int NumberOfFilesPendingIntake;
		public int NumberOfFilesPollingOpenHandles;
		public int NumberOfFilesPollingContentChanges;
		public int NumberOfBackupQueueActions;
		public int NumberOfQueuedUploads;

		public bool IsBackupAgentBusy =>
			(NumberOfFilesPendingIntake >= 0) ||
			(NumberOfFilesPollingOpenHandles >= 0) ||
			(NumberOfFilesPollingContentChanges >= 0) ||
			(NumberOfBackupQueueActions >= 0) ||
			(NumberOfQueuedUploads >= 0);
	}
}
