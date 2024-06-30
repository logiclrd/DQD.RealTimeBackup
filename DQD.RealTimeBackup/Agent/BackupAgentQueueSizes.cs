using System;
using System.Linq;

using DQD.RealTimeBackup.Bridge.Serialization;

namespace DQD.RealTimeBackup.Agent
{
	public class BackupAgentQueueSizes
	{
		[FieldOrder(0)]
		public int NumberOfFilesPendingIntake;
		[FieldOrder(1)]
		public int NumberOfFilesPollingOpenHandles;
		[FieldOrder(2)]
		public int NumberOfFilesPollingContentChanges;
		[FieldOrder(3)]
		public int NumberOfBackupQueueActions;
		[FieldOrder(4)]
		public int NumberOfQueuedUploads;

		[FieldOrder(5)]
		public UploadStatus?[]? UploadThreads;

		public bool IsBackupAgentBusy =>
			(NumberOfFilesPendingIntake > 0) ||
			(NumberOfFilesPollingOpenHandles > 0) ||
			(NumberOfFilesPollingContentChanges > 0) ||
			(NumberOfBackupQueueActions > 0) ||
			(NumberOfQueuedUploads > 0) ||
			((UploadThreads != null) && UploadThreads.OfType<UploadStatus>().Any());
	}
}
