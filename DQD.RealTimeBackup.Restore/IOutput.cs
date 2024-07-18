using System;

namespace DQD.RealTimeBackup.Restore
{
	public interface IOutput : IDisposable
	{
		IOutputFileList BeginList(string listName, string? directoryPath = null, bool isRecursive = false, bool? trustFileSizes = default);
		void EmitError(Exception e);
	}
}
