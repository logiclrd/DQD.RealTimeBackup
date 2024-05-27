using System;
using System.Xml;
using Microsoft.VisualBasic;

namespace DeltaQ.RTB.Restore
{
	public class XMLOutput : IOutput
	{
		XmlWriter _writer;

		public XMLOutput()
		{
			_writer = new XmlTextWriter(Console.Out);

			_writer.WriteStartElement("Output");
		}

		public void Dispose()
		{
			_writer.WriteEndElement();
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
