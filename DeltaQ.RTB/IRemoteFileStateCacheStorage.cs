using System;
using System.Collections.Generic;
using System.IO;

namespace DeltaQ.RTB
{
	public interface IRemoteFileStateCacheStorage
	{
		IEnumerable<int> EnumerateBatches();
		StreamWriter OpenBatchFileWriter(int batchNumber);
		StreamReader OpenBatchFileReader(int batchNumber);
		Stream OpenBatchFileStream(int batchNumber);
		StreamWriter OpenNewBatchFileWriter(int batchNumber);
		void SwitchToConsolidatedFile(int oldBatchNumber, int mergeIntoBatchNumber);
	}
}