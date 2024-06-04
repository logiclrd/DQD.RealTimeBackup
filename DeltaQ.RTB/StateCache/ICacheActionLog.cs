using System.Collections.Generic;

namespace DeltaQ.RTB.StateCache
{
	public interface ICacheActionLog
	{
		string ActionQueuePath { get; }

		void EnsureDirectoryExists();
		IEnumerable<long> EnumerateActionKeys();
		string GetQueueActionFileName(long key);
		void LogAction(CacheAction action);
		string CreateTemporaryCacheActionDataFile();
		CacheAction RehydrateAction(long key);
		void ReleaseAction(CacheAction action);
	}
}
