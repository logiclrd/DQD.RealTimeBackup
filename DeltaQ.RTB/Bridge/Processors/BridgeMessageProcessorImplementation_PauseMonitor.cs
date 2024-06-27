using System;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.FileSystem;

using DeltaQ.RTB.Bridge.Messages;

namespace DeltaQ.RTB.Bridge.Processors
{
	public class BridgeMessageProcessorImplementation_PauseMonitor : IBridgeMessageProcessorImplementation
	{
		public BridgeMessageType MessageType => BridgeMessageType.PauseMonitor_Request;

		IBackupAgent _backupAgent;
		IZFS _zfs;

		public BridgeMessageProcessorImplementation_PauseMonitor(IBackupAgent backupAgent, IZFS zfs)
		{
			_backupAgent = backupAgent;
			_zfs = zfs;
		}

		public bool IsLongRunning => false;

		public ProcessMessageResult? ProcessMessage(BridgeRequestMessage message)
		{
			var response = new BridgeMessage_PauseMonitor_Response();

			if (!(message is BridgeMessage_PauseMonitor_Request))
			{
				response.Error = new ErrorInfo { Message = "Internal error: message passed to ProcessMessage is not of the right type" };
				return ProcessMessageResult.Message(response);
			}

			try
			{
				_backupAgent.PauseMonitor();
			}
			catch (Exception ex)
			{
				response.Error = new ErrorInfo(ex);
			}			

			return ProcessMessageResult.Message(response);
		}
	}
}
