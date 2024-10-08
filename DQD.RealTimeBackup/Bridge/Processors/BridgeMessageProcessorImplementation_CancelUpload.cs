using System;

using DQD.RealTimeBackup.Agent;

using DQD.RealTimeBackup.Bridge.Messages;

namespace DQD.RealTimeBackup.Bridge.Processors
{
	public class BridgeMessageProcessorImplementation_CancelUpload : IBridgeMessageProcessorImplementation
	{
		public BridgeMessageType MessageType => BridgeMessageType.CancelUpload_Request;

		IBackupAgent _backupAgent;

		public BridgeMessageProcessorImplementation_CancelUpload(IBackupAgent backupAgent)
		{
			_backupAgent = backupAgent;
		}

		public bool IsLongRunning => false;

		public ProcessMessageResult? ProcessMessage(BridgeRequestMessage message)
		{
			var response = new BridgeMessage_CancelUpload_Response();

			if (!(message is BridgeMessage_CancelUpload_Request request))
			{
				response.Error = new ErrorInfo { Message = "Internal error: message passed to ProcessMessage is not of the right type" };
				return ProcessMessageResult.Message(response);
			}

			try
			{
				_backupAgent.CancelUpload(request.UploadThreadIndex);
			}
			catch (Exception ex)
			{
				response.Error = new ErrorInfo(ex);
			}			

			return ProcessMessageResult.Message(response);
		}
	}
}
