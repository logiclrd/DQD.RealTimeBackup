using System;
using System.Collections.Generic;
using System.IO;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.StateCache
{
	public interface IRemoteFileStateCacheStorage : IDiagnosticOutput
	{
		IEnumerable<int> EnumerateBatches();
		int GetBatchFileSize(int batchNumber);
		StreamWriter OpenBatchFileWriter(int batchNumber);
		StreamReader OpenBatchFileReader(int batchNumber);
		Stream OpenBatchFileStream(int batchNumber);
		StreamWriter OpenNewBatchFileWriter(int batchNumber);
		void SwitchToConsolidatedFile(IEnumerable<int> mergedBatchNumbersForDeletion, int mergeIntoBatchNumber);
	}
}
