using System;
using System.Xml;

namespace DQD.RealTimeBackup.Restore
{
	public class XMLOutput : IOutput
	{
		XmlWriter _writer;
		bool _isDisposed;

		bool _trustFileSizes;

		public XMLOutput(bool trustFileSizes)
		{
			_writer = new XmlTextWriter(Console.Out);

			_writer.WriteStartElement("Output");

			_trustFileSizes = trustFileSizes;
		}

		public void Dispose()
		{
			if (!_isDisposed)
			{
				_writer.WriteEndElement();
				_isDisposed = true;
			}
		}

		public IOutputFileList BeginList(string listName, string? directoryPath = null, bool isRecursive = false, bool? trustFileSizes = default)
		{
			return new XMLOutputFileList(_writer, listName, directoryPath, isRecursive, trustFileSizes ?? _trustFileSizes);
		}

		public void EmitError(Exception e)
		{
			Console.WriteLine();

			Console.Error.WriteLine("EXCEPTION:");
			Console.Error.WriteLine(e);
		}
	}
}
