using System;
using System.Xml;

namespace DeltaQ.RTB.Restore
{
	public class XMLOutput : IOutput
	{
		XmlWriter _writer;
		bool _isDisposed;

		public XMLOutput()
		{
			_writer = new XmlTextWriter(Console.Out);

			_writer.WriteStartElement("Output");
		}

		public void Dispose()
		{
			if (!_isDisposed)
			{
				_writer.WriteEndElement();
				_isDisposed = true;
			}
		}

		public IOutputFileList BeginList(string listName, string? directoryPath = null, bool isRecursive = false)
		{
			return new XMLOutputFileList(_writer, listName, directoryPath, isRecursive);
		}

		public void EmitError(Exception e)
		{
			Console.WriteLine();

			Console.Error.WriteLine("EXCEPTION:");
			Console.Error.WriteLine(e);
		}
	}
}
