namespace DQD.RealTimeBackup.Agent;

public enum PollOpenFilesThreadState
{
	Unknown,
	Idle,
	CollectingWait,
	EnumeratingHandles,
	InspectingFiles,
	PromotingFiles,
	Stopped,
	Crashed,
}
