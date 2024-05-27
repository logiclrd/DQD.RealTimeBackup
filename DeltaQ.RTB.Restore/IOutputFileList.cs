using System;

using DeltaQ.RTB.Storage;

namespace DeltaQ.RTB.Restore
{
	public interface IOutputFileList : IDisposable
	{
		void EmitFile(RemoteFileInfo fileInfo);
		void EmitFile(string filePath);
	}
}
