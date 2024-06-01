using System;
using DeltaQ.RTB.Agent;
using DeltaQ.RTB.FileSystem;

using DeltaQ.RTB.Bridge.Messages;

namespace DeltaQ.RTB.Bridge.Processors
{
	public class BridgeMessageProcessorImplementation_CheckPath : IBridgeMessageProcessorImplementation
	{
		public BridgeMessageType MessageType => BridgeMessageType.CheckPath_Request;

		IBackupAgent _backupAgent;
		IZFS _zfs;

		public BridgeMessageProcessorImplementation_CheckPath(IBackupAgent backupAgent, IZFS zfs)
		{
			_backupAgent = backupAgent;
			_zfs = zfs;
		}

		public ProcessMessageResult? ProcessMessage(BridgeMessage message)
		{
			var response = new BridgeMessage_CheckPath_Response();

			if (!(message is BridgeMessage_CheckPath_Request request))
			{
				response.Error = new ErrorInfo { Message = "Internal error: message passed to ProcessMessage is not of the right type" };
				return ProcessMessageResult.Message(response);
			}

			try
			{
				if (request.Path == null)
					throw new ArgumentNullException("request.Path");

				_backupAgent.CheckPath(request.Path);
			}
			catch (Exception ex)
			{
				response.Error = new ErrorInfo(ex);
			}			

			return ProcessMessageResult.Message(response);
		}
	}
}
