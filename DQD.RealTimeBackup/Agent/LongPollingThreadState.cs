namespace DQD.RealTimeBackup.Agent;

public enum LongPollingThreadState
{
	Unknown,
	Idle,
	EnumeratingHandles,
	ProcessingItems,
	QueuingActions,
	TrimmingQueue,
	Stopped,
	Crashed,
}
