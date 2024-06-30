using System;

namespace DQD.RealTimeBackup.Storage
{
	public class RemoteFileInfo
	{
		public string Path;
		public long FileSize;
		public DateTime LastModifiedUTC;
		public string RemoteFileID;

		public RemoteFileInfo(string path, long fileSize, DateTime lastModifiedUTC, string remoteFileID)
		{
			Path = path;
			FileSize = fileSize;
			LastModifiedUTC = lastModifiedUTC;
			RemoteFileID = remoteFileID;
		}
	}
}
