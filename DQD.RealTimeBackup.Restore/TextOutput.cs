using System;

namespace DQD.RealTimeBackup.Restore
{
	public class TextOutput : IOutput
	{
		public IOutputFileList BeginList(string listName, string? directoryPath = null, bool isRecursive = false)
		{
			return new TextOutputFileList(listName, directoryPath, isRecursive);
		}

		public void EmitError(Exception e)
		{
			Console.WriteLine();

			Console.Error.WriteLine("EXCEPTION:");
			Console.Error.WriteLine(e);
		}

		public void Dispose() { }
	}
}
