using System.Diagnostics;

namespace DeltaQ.RTB.StateCache
{
	public interface IRemoteFileStateCache
	{
		FileState? GetFileState(string path);
		void UpdateFileState(string path, FileState newFileState);
		void Stop();
		void WaitWhileBusy();
	}
}

