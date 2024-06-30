using System;
using System.Xml;

using DQD.RealTimeBackup.Storage;

namespace DQD.RealTimeBackup.Restore
{
	public class XMLOutputFileList : OutputFileListBase
	{
		XmlWriter _writer;

		public XMLOutputFileList(XmlWriter writer, string listName, string? path = null, bool isRecursive = false)
		{
			_writer = writer;

			_writer.WriteStartElement(listName.Replace(" ", ""));

			if (path != null)
				_writer.WriteAttributeString("Path", path);

			if (isRecursive)
				_writer.WriteAttributeString("IsRecursive", "true");
		}

		public override void Dispose()
		{
			_writer.WriteEndElement();
		}

		public override void EmitFile(RemoteFileInfo fileInfo)
		{
			_writer.WriteStartElement("File");

			_writer.WriteElementString("Path", fileInfo.Path);
			_writer.WriteElementString("FileSize",  fileInfo.FileSize.ToString());
			_writer.WriteElementString("LastModifiedUTC", fileInfo.LastModifiedUTC.ToString("o"));

			_writer.WriteEndElement();
		}
	}
}
