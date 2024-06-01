using System;
using System.Collections.Generic;

using DeltaQ.CommandLineParser;

namespace DeltaQ.RTB.Console
{
	public class CommandLineArguments
	{
		[Argument("/CONNECT", Description =
			"The endpoint to which to connect. Can be a UNIX socket (e.g. /run/DeltaQ.RTB/bridge.socket) " +
			"or a TCP/IP endpoint (e.g. 127.0.0.1:12345 -- check /run/DeltaQ.RTB/bridge-tcp-port for the " +
			"port number, which may be dynamically-assigned).")]
		public string? ConnectTo;

		[Switch("/GETSTATS", Description =
			"Requests operating statistics from the Backup Agent.")]
		public bool GetStats;

		[Switch("/GETSTATSINCLUDEUPLOADS", Description =
			"If false, then detailed information about the upload threads is suppressed. Defaults to true.")]
		public bool GetStatsIncludeUploads;

		[Switch("/GETSTATSREPEAT", Description =
			"Gets operating statistics repeatedly in a loop until the process is terminated.")]
		public bool GetStatsRepeat;

		[Argument("/CHECKPATH", Description =
			"Submits a file path to be checked for changes (including deletion). Can be specified multiple " +
			"times.")]
		public List<string> CheckFilePaths = new List<string>();

		[Switch("/PAUSEMONITOR", Description =
			"Instructs the Backup Agent to pause monitoring. If it is already paused, there is no effect.")]
		public bool PauseMonitor;

		[Switch("/UNPAUSEMONITOR", Description =
			"Instructs the Backup Agent to unpause monitoring. If it is not currently paused, there is no " +
			"effect.")]
		public bool UnpauseMonitor;

		[Switch("/DISCARDBUFFEREDNOTIFICATIONS", Description =
			"When using /UNPAUSEMONITOR, instructs the Backup Agent to discard any buffered paths for which " +
			"events were received while paused. By default, these paths are queued for processing as soon as " +
			"File Monitoring in unpaused.")]
		public bool DiscardBufferedNotifications;

		[Switch("/PERFORMRESCAN", Description =
			"Instructs the DeltaQ.RTB service to begin a rescan. This scan is exactly the same " +
			"as the periodic rescan that is started on a timer to check for differences between the " +
			"actual state of files on the filesystem and the state stored in remote storage. If a rescan " +
			"is already underway, then this operation has no effect.")]
		public bool PerformRescan;

		[Switch("/GETRESCANSTATUS", Description =
			"Retrieves a status update for any currently-running rescan.")]
		public bool GetRescanStatus;

		[Switch("/FOLLOWRESCAN", Description =
			"Stays attached to the Backup Agent while a periodic rescan runs, displaying ongoing " +
			"statistics about the scan. When the scan completes, DeltaQ.RTB.Console exits. This " +
			"switch will follow a scan started with /PERFORMRESCAN, but it can also connect to rescans " +
			"that are already underway.")]
		public bool FollowRescan;

		[Switch("/CANCELRESCAN", Description =
			"Instructs the DeltaQ.RTB to stop any ongoing rescan, whether it was started by the " +
			"periodic rescan scheduler or explicitly by the user. If no scan is currently in progress " +
			"this command has no effect.")]
		public bool CancelRescan;

		[Switch("/XML", Description =
			"Write all output in XML, making it machine-readable.")]
		public bool XML = false;

		[Switch("/?")]
		public bool ShowUsage;

		public bool Persistent => GetStats && GetStatsRepeat;
	}
}
