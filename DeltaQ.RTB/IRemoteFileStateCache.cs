namespace DeltaQ.RTB
{
	public interface IRemoteFileStateCache
	{
		FileState? GetFileState(string path);
		void UpdateFileState(string path, FileState newFileState);
	}
}

