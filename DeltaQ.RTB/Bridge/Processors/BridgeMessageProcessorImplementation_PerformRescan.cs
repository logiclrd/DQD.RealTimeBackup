using System;

using DeltaQ.RTB.Bridge.Messages;
using DeltaQ.RTB.Scan;

namespace DeltaQ.RTB.Bridge.Processors
{
	public class BridgeMessageProcessorImplementation_PerformRescan : IBridgeMessageProcessorImplementation
	{
		public BridgeMessageType MessageType => BridgeMessageType.PerformRescan_Request;

		IPeriodicRescanScheduler _periodicRescanScheduler;

		public BridgeMessageProcessorImplementation_PerformRescan(IPeriodicRescanScheduler periodicRescanScheduler)
		{
			_periodicRescanScheduler = periodicRescanScheduler;
		}

		public bool IsLongRunning => false;

		public ProcessMessageResult? ProcessMessage(BridgeRequestMessage message)
		{
			var response = new BridgeMessage_PerformRescan_Response();

			if (!(message is BridgeMessage_PerformRescan_Request))
			{
				response.Error = new ErrorInfo { Message = "Internal error: message passed to ProcessMessage is not of the right type" };
				return ProcessMessageResult.Message(response);
			}

			try
			{
				var result = _periodicRescanScheduler.PerformRescanNow();

				response.RescanNumber = result.RescanNumber;
				response.AlreadyRunning = result.AlreadyRunning;
			}
			catch (Exception ex)
			{
				response.Error = new ErrorInfo(ex);
			}			

			return ProcessMessageResult.Message(response);
		}
	}
}
