using System;
using System.Collections.Generic;
using System.IO;

using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.StateCache
{
	public class RemoteFileStateCacheStorage : DiagnosticOutputBase, IRemoteFileStateCacheStorage
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

		public int GetBatchFileSize(int batchNumber)
		{
			var path = Path.Combine(
				_parameters.RemoteFileStateCachePath,
				batchNumber.ToString());

			try
			{
				return (int)new FileInfo(path).Length;
			}
			catch
			{
				return -1;
			}
		}

		public StreamWriter OpenBatchFileWriter(int batchNumber)
		{
			var path = Path.Combine(
				_parameters.RemoteFileStateCachePath,
				batchNumber.ToString());

			VerboseDiagnosticOutput("[RFSCS] OpenBatchFileWriter, path is {0}", path);

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

		public void SwitchToConsolidatedFile(IEnumerable<int> mergedBatchNumbersForDeletion, int mergeIntoBatchNumber)
		{
			string mergeIntoBatchPath = Path.Combine(_parameters.RemoteFileStateCachePath, mergeIntoBatchNumber.ToString());
			
			File.Move(mergeIntoBatchPath + ".new", mergeIntoBatchPath, overwrite: true);

			foreach (int batchNumberToDelete in mergedBatchNumbersForDeletion)
			{
				string batchToDeletePath = Path.Combine(_parameters.RemoteFileStateCachePath, batchNumberToDelete.ToString());

				File.Delete(batchToDeletePath);
			}
		}
	}
}
