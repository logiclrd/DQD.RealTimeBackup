using System;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.FileSystem;

using DQD.RealTimeBackup.Bridge.Messages;

namespace DQD.RealTimeBackup.Bridge.Processors
{
	public class BridgeMessageProcessorImplementation_GetStats : IBridgeMessageProcessorImplementation
	{
		public BridgeMessageType MessageType => BridgeMessageType.GetStats_Request;

		IBackupAgent _backupAgent;
		IZFS _zfs;

		public BridgeMessageProcessorImplementation_GetStats(IBackupAgent backupAgent, IZFS zfs)
		{
			_backupAgent = backupAgent;
			_zfs = zfs;
		}

		public bool IsLongRunning => false;

		public ProcessMessageResult? ProcessMessage(BridgeRequestMessage message)
		{
			var response = new BridgeMessage_GetStats_Response();

			if (!(message is BridgeMessage_GetStats_Request request))
			{
				response.Error = new ErrorInfo { Message = "Internal error: message passed to ProcessMessage is not of the right type" };
				return ProcessMessageResult.Message(response);
			}

			try
			{
				response.BackupAgentQueueSizes = _backupAgent.GetQueueSizes();
				response.ZFSSnapshotCount = _zfs.CurrentSnapshotCount;
			}
			catch (Exception ex)
			{
				response.Error = new ErrorInfo(ex);
			}

			return ProcessMessageResult.Message(response);
		}
	}
}
