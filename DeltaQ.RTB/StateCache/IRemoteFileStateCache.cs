using System.Diagnostics;

namespace DeltaQ.RTB.StateCache
{
	public interface IRemoteFileStateCache
	{
		bool ContainsPath(string path);
		FileState? GetFileState(string path);
		void UpdateFileState(string path, FileState newFileState);
		bool RemoveFileState(string path);
		void Stop();
		void WaitWhileBusy();
		void UploadCurrentBatchAndBeginNext(bool deferConsolidation = false);
	}
}

