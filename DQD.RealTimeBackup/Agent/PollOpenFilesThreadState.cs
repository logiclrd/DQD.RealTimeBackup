namespace DQD.RealTimeBackup.Agent;

public enum PollOpenFilesThreadState
{
	Unknown,
	Idle,
	CollectingWait,
	EnumeratingHandles,
	InspectingFiles,
	PromotingFiles_ObtainingLock,
	PromotingFiles,
	Stopped,
	Crashed,
}
