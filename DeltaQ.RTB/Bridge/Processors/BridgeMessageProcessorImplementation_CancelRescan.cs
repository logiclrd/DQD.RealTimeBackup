using System;

using DeltaQ.RTB.Bridge.Messages;
using DeltaQ.RTB.Scan;

namespace DeltaQ.RTB.Bridge.Processors
{
	public class BridgeMessageProcessorImplementation_CancelRescan : IBridgeMessageProcessorImplementation
	{
		public BridgeMessageType MessageType => BridgeMessageType.CancelRescan_Request;

		IPeriodicRescanScheduler _periodicRescanScheduler;

		public BridgeMessageProcessorImplementation_CancelRescan(IPeriodicRescanScheduler periodicRescanScheduler)
		{
			_periodicRescanScheduler = periodicRescanScheduler;
		}

		public bool IsLongRunning => false;

		public ProcessMessageResult? ProcessMessage(BridgeRequestMessage message)
		{
			var response = new BridgeMessage_CancelRescan_Response();

			if (!(message is BridgeMessage_CancelRescan_Request))
			{
				response.Error = new ErrorInfo { Message = "Internal error: message passed to ProcessMessage is not of the right type" };
				return ProcessMessageResult.Message(response);
			}

			try
			{
				_periodicRescanScheduler.CancelRescan();
			}
			catch (Exception ex)
			{
				response.Error = new ErrorInfo(ex);
			}			

			return ProcessMessageResult.Message(response);
		}
	}
}
