namespace DeltaQ.RTB.Bridge.Messages
{
	public enum BridgeMessageType
	{
		Unknown,

		GetStats_Request,
		GetStats_Response,

		CheckPath_Request,
		CheckPath_Response,

		PauseMonitor_Request,
		PauseMonitor_Response,

		UnpauseMonitor_Request,
		UnpauseMonitor_Response,
	}
}
