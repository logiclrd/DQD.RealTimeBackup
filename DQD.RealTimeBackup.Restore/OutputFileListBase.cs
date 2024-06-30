using System.IO;

using DQD.RealTimeBackup.Storage;

namespace DQD.RealTimeBackup.Restore
{
	public abstract class OutputFileListBase : IOutputFileList
	{
		public void EmitFile(string filePath)
		{
			try
			{
				var fileInfo = new FileInfo(filePath);

				EmitFile(new RemoteFileInfo(
					filePath,
					fileInfo.Length,
					fileInfo.LastWriteTimeUtc,
					remoteFileID: ""));
			}
			catch
			{
				EmitFile(new RemoteFileInfo(filePath, default, default, ""));
			}
		}

		public abstract void EmitFile(RemoteFileInfo fileInfo);
		public abstract void Dispose();
	}
}
