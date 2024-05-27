using System;

using DeltaQ.RTB.Storage;

namespace DeltaQ.RTB.Restore
{
	public class TextOutputFileList : OutputFileListBase
	{
		int _fileFieldsStartColumn;
		string _fileFieldsIndent;

		public TextOutputFileList(string listName, string? path = null, bool isRecursive = false)
		{
			int fileSizeWidth = 17; // Allows files up to 9.9 TB in size.
			int dateWidth = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt").Length;

			int lineWidth = Console.IsOutputRedirected ? 150 : Console.WindowWidth;

			_fileFieldsStartColumn = lineWidth - 1 - dateWidth - 2 - fileSizeWidth;

			_fileFieldsIndent = new string(' ', Math.Max(2, _fileFieldsStartColumn));

			if (path == null)
				Console.WriteLine(listName);
			else
				Console.WriteLine("{0}: files in {1}", listName, path);

			if (isRecursive)
				Console.WriteLine("(recursive)");
		}

		public override void EmitFile(RemoteFileInfo fileInfo)
		{
			Console.Write(fileInfo.Path);

			if (fileInfo.Path.Length + 2 > _fileFieldsStartColumn)
			{
				if (!Console.IsOutputRedirected)
				{
					Console.WriteLine();

					Console.Write(_fileFieldsIndent);
				}
			}
			else
			{
				for (int i = fileInfo.Path.Length; i < _fileFieldsStartColumn; i++)
					Console.Write(' ');
			}

			Console.Write(fileInfo.FileSize.ToString("#,###,###,###,##0").PadLeft(17));
			Console.Write("  ");
			Console.WriteLine(fileInfo.LastModifiedUTC.ToString("yyyy-MM-dd HH:mm:ss tt"));
		}

		public override void Dispose() { }
	}
}
