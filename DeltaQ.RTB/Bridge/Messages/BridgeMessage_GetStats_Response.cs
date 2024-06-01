using DeltaQ.RTB.Agent;
using DeltaQ.RTB.Storage;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_GetStats_Response : BridgeResponseMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.GetStats_Response;

		public BackupAgentQueueSizes? BackupAgentQueueSizes;
		public int ZFSSnapshotCount;

		protected override void SerializeResponseImplementation(ByteBuffer buffer)
		{
			SerializeBackupAgentQueueSizes(buffer, BackupAgentQueueSizes);
			buffer.AppendInt32(ZFSSnapshotCount);
		}

		protected override void DeserializeResponseImplementation(ByteBuffer buffer)
		{
			BackupAgentQueueSizes = DeserializeBackupAgentQueueSizes(buffer);
			ZFSSnapshotCount = buffer.ReadInt32(); 
		}

		void SerializeBackupAgentQueueSizes(ByteBuffer buffer, BackupAgentQueueSizes? backupAgentQueueSizes)
		{
			if (backupAgentQueueSizes == null)
				buffer.AppendByte(0);
			else
			{
				buffer.AppendByte(1);

				buffer.AppendInt32(backupAgentQueueSizes.NumberOfFilesPendingIntake);
				buffer.AppendInt32(backupAgentQueueSizes.NumberOfFilesPollingOpenHandles);
				buffer.AppendInt32(backupAgentQueueSizes.NumberOfFilesPollingContentChanges);
				buffer.AppendInt32(backupAgentQueueSizes.NumberOfBackupQueueActions);
				buffer.AppendInt32(backupAgentQueueSizes.NumberOfQueuedUploads);

				if (backupAgentQueueSizes.UploadThreads == null)
					buffer.AppendByte(0);
				else
				{
					buffer.AppendByte(1);
					buffer.AppendInt32(backupAgentQueueSizes.UploadThreads!.Length);

					for (int i=0; i < backupAgentQueueSizes.UploadThreads.Length; i++)
						SerializeUploadStatus(buffer, backupAgentQueueSizes.UploadThreads[i]);
				}
			}
		}

		BackupAgentQueueSizes? DeserializeBackupAgentQueueSizes(ByteBuffer buffer)
		{
			if (buffer.ReadByte() == 0)
				return null;

			var backupAgentQueueSizes = new BackupAgentQueueSizes();

			backupAgentQueueSizes.NumberOfFilesPendingIntake = buffer.ReadInt32();
			backupAgentQueueSizes.NumberOfFilesPollingOpenHandles = buffer.ReadInt32();
			backupAgentQueueSizes.NumberOfFilesPollingContentChanges = buffer.ReadInt32();
			backupAgentQueueSizes.NumberOfBackupQueueActions = buffer.ReadInt32();
			backupAgentQueueSizes.NumberOfQueuedUploads = buffer.ReadInt32();

			if (buffer.ReadByte() != 0)
			{
				int numUploadThreads = buffer.ReadInt32();

				backupAgentQueueSizes.UploadThreads = new UploadStatus?[numUploadThreads];

				for (int i=0; i < numUploadThreads; i++)
					backupAgentQueueSizes.UploadThreads[i] = DeserializeUploadStatus(buffer);
			}

			return backupAgentQueueSizes;
		}

		void SerializeUploadStatus(ByteBuffer buffer, UploadStatus? uploadStatus)
		{
			if (uploadStatus == null)
				buffer.AppendByte(0);
			else
			{
				buffer.AppendByte(1);

				buffer.AppendString(uploadStatus.Path);
				buffer.AppendInt64(uploadStatus.FileSize);

				SerializeUploadProgress(buffer, uploadStatus.Progress);
			}
		}

		UploadStatus? DeserializeUploadStatus(ByteBuffer buffer)
		{
			if (buffer.ReadByte() == 0)
				return null;

			var path = buffer.ReadString();
			var fileSize = buffer.ReadInt64();

			var uploadStatus = new UploadStatus(path, fileSize);

			uploadStatus.Progress = DeserializeUploadProgress(buffer);

			return uploadStatus;
		}

		void SerializeUploadProgress(ByteBuffer buffer, UploadProgress? uploadProgress)
		{
			if (uploadProgress == null)
				buffer.AppendByte(0);
			else
			{
				buffer.AppendByte(1);

				buffer.AppendInt64(uploadProgress.BytesPerSecond);
				buffer.AppendInt64(uploadProgress.BytesTransferred);
			}
		}

		UploadProgress? DeserializeUploadProgress(ByteBuffer buffer)
		{
			if (buffer.ReadByte() == 0)
				return null;

			var uploadProgress = new UploadProgress();

			uploadProgress.BytesPerSecond = buffer.ReadInt64();
			uploadProgress.BytesTransferred = buffer.ReadInt64();

			return uploadProgress;
		}
	}
}
