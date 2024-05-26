using System.Collections.Generic;

namespace DeltaQ.RTB.StateCache
{
	public interface IRemoteFileStateCache
	{
		bool ContainsPath(string path);
		IEnumerable<string> EnumeratePaths();
		FileState? GetFileState(string path);
		void UpdateFileState(string path, FileState newFileState);
		bool RemoveFileState(string path);
		void Start();
		void Stop();
		void WaitWhileBusy();
		void UploadCurrentBatchAndBeginNext(bool deferConsolidation = false);
	}
}

