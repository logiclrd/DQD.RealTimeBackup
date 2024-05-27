using System;

namespace DeltaQ.RTB.Restore
{
	public interface IOutput : IDisposable
	{
		IOutputFileList BeginList(string listName, string? directoryPath = null, bool isRecursive = false);
		void EmitError(Exception e);
	}
}
