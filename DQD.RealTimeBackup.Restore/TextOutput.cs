using System;

namespace DQD.RealTimeBackup.Restore
{
	public class TextOutput : IOutput
	{
		bool _trustFileSizes;

		public TextOutput(bool trustFileSizes)
		{
			_trustFileSizes = trustFileSizes;
		}

		public IOutputFileList BeginList(string listName, string? directoryPath = null, bool isRecursive = false, bool? trustFileSizes = default)
		{
			return new TextOutputFileList(listName, directoryPath, isRecursive, trustFileSizes ?? _trustFileSizes);
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
