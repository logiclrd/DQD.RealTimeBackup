using System.Collections.Generic;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.StateCache
{
	public interface IRemoteFileStateCache : IDiagnosticOutput
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

