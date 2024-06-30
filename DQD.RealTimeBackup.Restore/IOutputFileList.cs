using System;

using DQD.RealTimeBackup.Storage;

namespace DQD.RealTimeBackup.Restore
{
	public interface IOutputFileList : IDisposable
	{
		void EmitFile(RemoteFileInfo fileInfo);
		void EmitFile(string filePath);
	}
}
