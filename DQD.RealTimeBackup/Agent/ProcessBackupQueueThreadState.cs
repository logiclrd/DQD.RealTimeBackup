namespace DQD.RealTimeBackup.Agent;

public enum ProcessBackupQueueThreadState
{
	Unknown,
	Idle,
	ProcessingAction,
	ProcessingAction_OpeningFile,
	ProcessingAction_StagingFile,
	ProcessingAction_QueuingUpload,
	ProcessingAction_MovingFile,
	ProcessingAction_RegisteringFileMove,
	ProcessingAction_DeletingFile,
	ProcessingAction_RegisteringFileDeletion,
	Paused,
	Stopped,
	Crashed,
}