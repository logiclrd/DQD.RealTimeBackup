using System.IO;

using DeltaQ.RTB.Storage;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Restore
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
					fileInfo.LastWriteTimeUtc));
			}
			catch
			{
				EmitFile(new RemoteFileInfo(filePath, default, default));
			}
		}

		public abstract void EmitFile(RemoteFileInfo fileInfo);
		public abstract void Dispose();
	}
}
