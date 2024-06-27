using System;

using DeltaQ.RTB.Scan;

using DeltaQ.RTB.Bridge.Messages;

namespace DeltaQ.RTB.Bridge.Processors
{
	public class BridgeMessageProcessorImplementation_GetRescanStatus : IBridgeMessageProcessorImplementation
	{
		public BridgeMessageType MessageType => BridgeMessageType.GetRescanStatus_Request;

		IPeriodicRescanScheduler _periodicRescanScheduler;

		public BridgeMessageProcessorImplementation_GetRescanStatus(IPeriodicRescanScheduler periodicRescanScheduler)
		{
			_periodicRescanScheduler = periodicRescanScheduler;
		}

		public bool IsLongRunning => true;

		public ProcessMessageResult? ProcessMessage(BridgeRequestMessage message)
		{
			var response = new BridgeMessage_GetRescanStatus_Response();

			if (!(message is BridgeMessage_GetRescanStatus_Request getRescanStatusRequest))
			{
				response.Error = new ErrorInfo { Message = "Internal error: message passed to ProcessMessage is not of the right type" };
				return ProcessMessageResult.Message(response);
			}

			try
			{
				response.RescanStatus = _periodicRescanScheduler.GetRescanStatus(getRescanStatusRequest.Wait);
			}
			catch (Exception ex)
			{
				response.Error = new ErrorInfo(ex);
			}			

			return ProcessMessageResult.Message(response);
		}
	}
}
