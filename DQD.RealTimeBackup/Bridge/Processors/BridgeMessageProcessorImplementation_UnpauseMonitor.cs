using System;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.FileSystem;

using DQD.RealTimeBackup.Bridge.Messages;

namespace DQD.RealTimeBackup.Bridge.Processors
{
	public class BridgeMessageProcessorImplementation_UnpauseMonitor : IBridgeMessageProcessorImplementation
	{
		public BridgeMessageType MessageType => BridgeMessageType.UnpauseMonitor_Request;

		IBackupAgent _backupAgent;
		IZFS _zfs;

		public BridgeMessageProcessorImplementation_UnpauseMonitor(IBackupAgent backupAgent, IZFS zfs)
		{
			_backupAgent = backupAgent;
			_zfs = zfs;
		}

		public bool IsLongRunning => false;

		public ProcessMessageResult? ProcessMessage(BridgeRequestMessage message)
		{
			var response = new BridgeMessage_UnpauseMonitor_Response();

			if (!(message is BridgeMessage_UnpauseMonitor_Request unpauseRequest))
			{
				response.Error = new ErrorInfo { Message = "Internal error: message passed to ProcessMessage is not of the right type" };
				return ProcessMessageResult.Message(response);
			}

			try
			{
				_backupAgent.UnpauseMonitor(unpauseRequest.ProcessBufferedPaths);
			}
			catch (Exception ex)
			{
				response.Error = new ErrorInfo(ex);
			}			

			return ProcessMessageResult.Message(response);
		}
	}
}
