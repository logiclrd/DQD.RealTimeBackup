namespace DQD.RealTimeBackup.StateCache;

public enum RemoteFileStateCacheActionThreadState
{
	Unknown,
	Idle,
	HaveAction_CheckIfRedundant,
	HaveAction_IsRedundant,
	HaveAction_Processing,
	HaveAction_FinishedProcessing,
	HaveAction_Aborted,
	DeletingActionFile,
	Stopped,
	Crashed,
}