namespace DQD.RealTimeBackup.Bridge.Messages
{
	public enum BridgeMessageType
	{
		Unknown,

		ReceiveNotifications_Request,
		ReceiveNotifications_Response,

		GetStats_Request,
		GetStats_Response,

		CheckPath_Request,
		CheckPath_Response,

		PauseMonitor_Request,
		PauseMonitor_Response,

		UnpauseMonitor_Request,
		UnpauseMonitor_Response,

		PerformRescan_Request,
		PerformRescan_Response,

		CancelRescan_Request,
		CancelRescan_Response,

		GetRescanStatus_Request,
		GetRescanStatus_Response,

		CancelUpload_Request,
		CancelUpload_Response,
	}
}
