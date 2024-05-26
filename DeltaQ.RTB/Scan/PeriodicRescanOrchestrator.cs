using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.FileSystem;
using DeltaQ.RTB.Interop;
using DeltaQ.RTB.StateCache;
using DeltaQ.RTB.SurfaceArea;

namespace DeltaQ.RTB.Scan
{
	public class PeriodicRescanOrchestrator : Scanner, IPeriodicRescanOrchestrator
	{
		OperatingParameters _parameters;

		IBackupAgent _backupAgent;
		IRemoteFileStateCache _remoteFileStateCache;

		public PeriodicRescanOrchestrator(OperatingParameters parameters, ISurfaceArea surfaceArea, IBackupAgent backupAgent, IRemoteFileStateCache remoteFileStateCache, IZFS zfs, IStat stat)
			: base(parameters, surfaceArea, stat)
		{
			_parameters = parameters;

			_backupAgent = backupAgent;
			_remoteFileStateCache = remoteFileStateCache;
		}

		public void PerformPeriodicRescan(CancellationToken cancellationToken)
		{
			foreach (var path in EnumerateAllFilesInSurfaceArea())
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				var fileState = _remoteFileStateCache.GetFileState(path);

				bool checkPath = false;

				if (fileState == null)
					checkPath = true;
				else
				{
					var fileInfo = new FileInfo(fileState.Path);
					
					if ((fileState.FileSize != fileInfo.Length)
					 || (fileState.LastModifiedUTC != fileInfo.LastWriteTimeUtc))
						checkPath = true;
				}

				if (checkPath)
				{
					_backupAgent.CheckPath(path);

					if (_backupAgent.OpenFilesCount >= _parameters.QueueHighWaterMark)
					{
						while (_backupAgent.OpenFilesCount >= _parameters.QueueLowWaterMark)
							Thread.Sleep(TimeSpan.FromSeconds(10));
					}
				}
			}
		}
	}
}
