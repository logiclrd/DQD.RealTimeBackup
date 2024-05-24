using System;
using System.Collections.Generic;
using System.IO;

namespace DeltaQ.RTB.StateCache
{
	public class RemoteFileStateCacheStorage : IRemoteFileStateCacheStorage
	{
		OperatingParameters _parameters;

		public RemoteFileStateCacheStorage(OperatingParameters parameters)
		{
			_parameters = parameters;

			Directory.CreateDirectory(_parameters.RemoteFileStateCachePath);
		}

		public IEnumerable<int> EnumerateBatches()
		{
			foreach (var batchFile in Directory.EnumerateFiles(_parameters.RemoteFileStateCachePath))
				if (int.TryParse(Path.GetFileName(batchFile), out var batchNumber))
					yield return batchNumber;
		}

		public StreamWriter OpenBatchFileWriter(int batchNumber)
		{
			var path = Path.Combine(
				_parameters.RemoteFileStateCachePath,
				batchNumber.ToString());

			if (_parameters.Verbose)
				Console.WriteLine("[RFSCS] OpenBatchFileWriter, path is {0}", path);

			return new StreamWriter(path);
		}

		public StreamReader OpenBatchFileReader(int batchNumber)
		{
			return new StreamReader(
				Path.Combine(
					_parameters.RemoteFileStateCachePath,
					batchNumber.ToString()));
		}

		public Stream OpenBatchFileStream(int batchNumber)
		{
			return File.OpenRead(
				Path.Combine(
					_parameters.RemoteFileStateCachePath,
					batchNumber.ToString()));
		}

		public StreamWriter OpenNewBatchFileWriter(int batchNumber)
		{
			return new StreamWriter(
				Path.Combine(
					_parameters.RemoteFileStateCachePath,
					batchNumber + ".new"));
		}

		public void SwitchToConsolidatedFile(int oldBatchNumber, int mergeIntoBatchNumber)
		{
			string oldestBatchPath = Path.Combine(_parameters.RemoteFileStateCachePath, oldBatchNumber.ToString());
			string mergeIntoBatchPath = Path.Combine(_parameters.RemoteFileStateCachePath, mergeIntoBatchNumber.ToString());
			
			File.Move(mergeIntoBatchPath + ".new", mergeIntoBatchPath, overwrite: true);
			File.Delete(oldestBatchPath);
		}
	}
}
