using System;

namespace DeltaQ.RTB.Storage
{
	public class RemoteFileInfo
	{
		public string Path;
		public long FileSize;
		public DateTime LastModifiedUTC;

		public RemoteFileInfo(string path, long fileSize, DateTime lastModifiedUTC)
		{
			Path = path;
			FileSize = fileSize;
			LastModifiedUTC = lastModifiedUTC;
		}
	}
}
