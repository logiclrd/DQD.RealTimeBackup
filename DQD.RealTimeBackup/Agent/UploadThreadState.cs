namespace DQD.RealTimeBackup.Agent;

public enum UploadThreadState
{
	Unknown,
	Idle,
	ProcessingUpload,
	ProcessingUpload_OpeningFile,
	ProcessingUpload_UploadingEntireFile,
	ProcessingUpload_UploadingFileInParts,
	ProcessingUpload_DeletingUnneededParts,
	ProcessingUpload_RegisteringFileStateChange,
	Paused,
	Stopped,
	Crashed,
}