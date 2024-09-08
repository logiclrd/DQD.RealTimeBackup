using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;

using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.StateCache
{
	public class RemoteFileStateCacheRemoteStorage : DiagnosticOutputBase, IRemoteFileStateCacheStorage
	{
		OperatingParameters _parameters;

		IRemoteStorage _remoteStorage;

		public RemoteFileStateCacheRemoteStorage(OperatingParameters parameters, IRemoteStorage remoteStorage)
		{
			_parameters = parameters;

			_remoteStorage = remoteStorage;
		}

		public IEnumerable<BatchFileInfo> EnumerateBatches()
		{
			var stateFiles = _remoteStorage.EnumerateFiles("/state/", recursive: false).ToList();

			foreach (var stateFile in stateFiles)
			{
				var info = new BatchFileInfo();

				info.Path = stateFile.Path;

				if (int.TryParse(Path.GetFileName(info.Path), out var batchNumber))
					info.BatchNumber = batchNumber;

				info.FileSize = stateFile.FileSize;

				yield return info;
			}
		}

		public int GetBatchFileSize(int batchNumber)
		{
			throw new NotSupportedException();
		}

		public StreamWriter OpenBatchFileWriter(int batchNumber)
		{
			throw new NotSupportedException();
		}

		public StreamReader OpenBatchFileReader(int batchNumber)
		{
			return new StreamReader(OpenBatchFileStream(batchNumber));
		}

		public Stream OpenBatchFileStream(int batchNumber)
		{
			string batchFileName = "/state/" + batchNumber;

			var pipeOut = new AnonymousPipeServerStream(PipeDirection.Out);
			var pipeIn = new AnonymousPipeClientStream(PipeDirection.In, pipeOut.ClientSafePipeHandle);

			_remoteStorage.DownloadFileDirectAsync(batchFileName, pipeOut, CancellationToken.None)
				.ContinueWith(
					_ =>
					{
						pipeOut.Close();
					});

			return pipeIn;
		}

		public StreamWriter OpenNewBatchFileWriter(int batchNumber)
		{
			throw new NotSupportedException();
		}

		public void SwitchToConsolidatedFile(IEnumerable<int> mergedBatchNumbersForDeletion, int mergeIntoBatchNumber)
		{
			throw new NotSupportedException();
		}
	}
}
