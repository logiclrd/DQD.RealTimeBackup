using System;
using System.Xml;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.Bridge.Messages;
using DeltaQ.RTB.Storage;

using Konsole = System.Console;

namespace DeltaQ.RTB.Console.Formatters
{
	public class XMLOutputFormatter : IOutputFormatter
	{
		XmlWriter _writer;

		public XMLOutputFormatter()
		{
			_writer = new XmlTextWriter(Konsole.Out);
			_writer.WriteStartElement("RTBOutput");
		}

		public void EmitGetStatsHeading()
		{
		}

		public void EmitGetStatsResponse(BridgeMessage_GetStats_Response message)
		{
			_writer.WriteStartElement("Statistics");
			_writer.WriteAttributeString("TimestampUTC", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"));
			_writer.WriteAttributeString("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
			
			{
				_writer.WriteStartElement("BackupAgentQueueSizes");

				{
					if (!(message.BackupAgentQueueSizes is BackupAgentQueueSizes queueSizes))
						_writer.WriteAttributeString("xsi:nil", "true");
					else
					{
						_writer.WriteElementString("NumberOfFilesPendingIntake", queueSizes.NumberOfFilesPendingIntake.ToString());
						_writer.WriteElementString("NumberOfFilesPollingOpenHandles", queueSizes.NumberOfFilesPollingOpenHandles.ToString());
						_writer.WriteElementString("NumberOfFilesPollingContentChanges", queueSizes.NumberOfFilesPollingContentChanges.ToString());
						_writer.WriteElementString("NumberOfBackupQueueActions", queueSizes.NumberOfBackupQueueActions.ToString());
						_writer.WriteElementString("NumberOfQueuedUploads", queueSizes.NumberOfQueuedUploads.ToString());
					}

					_writer.WriteEndElement();
				}

				_writer.WriteElementString("ZFSSnapshotCount", message.ZFSSnapshotCount.ToString());
			}

			_writer.WriteEndElement();
		}

		public void EmitUploadThreads(UploadStatus?[] uploadThreads)
		{
			_writer.WriteStartElement("UploadThreads");
			_writer.WriteAttributeString("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");

			for (int i=0; i < uploadThreads.Length; i++)
			{
				_writer.WriteStartElement("UploadThread");
				_writer.WriteAttributeString("Index", i.ToString());

				if (!(uploadThreads[i] is UploadStatus uploadThreadStatus))
					_writer.WriteAttributeString("xsi:nil", "true");
				else
				{
					_writer.WriteAttributeString("Path", uploadThreadStatus.Path);
					_writer.WriteAttributeString("FileSize", uploadThreadStatus.FileSize.ToString());

					if (uploadThreadStatus.Progress is UploadProgress uploadProgress)
					{
						_writer.WriteAttributeString("BytesTransferred", uploadProgress.BytesTransferred.ToString());
						_writer.WriteAttributeString("BytesPerSecond", uploadProgress.BytesPerSecond.ToString());
					}
				}

				_writer.WriteEndElement();
			}

			_writer.WriteEndElement();
		}

		public void EmitPathSubmittedForCheck(string path)
		{
			_writer.WriteStartElement("PathSubmitted");
			_writer.WriteAttributeString("TimestampUTC", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"));
			_writer.WriteString(path);
			_writer.WriteEndElement();
		}

		public void EmitMonitorPaused()
		{
			_writer.WriteStartElement("MonitorPaused");
			_writer.WriteAttributeString("TimestampUTC", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"));
			_writer.WriteEndElement();
		}

		public void EmitMonitorUnpaused()
		{
			_writer.WriteStartElement("MonitorUnpaused");
			_writer.WriteAttributeString("TimestampUTC", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"));
			_writer.WriteEndElement();
		}

		public void EmitError(Exception ex)
		{
			EmitError(ex, "Exception", timestamp: true);
		}

		private void EmitError(Exception ex, string elementName, bool timestamp)
		{
			_writer.WriteStartElement(elementName);
			if (timestamp)
				_writer.WriteAttributeString("TimestampUTC", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"));
			_writer.WriteAttributeString("FullTypeName", ex.GetType().FullName);
			_writer.WriteElementString("Message", ex.Message);

			if (ex.StackTrace != null)
			{
				string[] lines = ex.StackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

				_writer.WriteStartElement("StackTrace");

				foreach (var line in lines)
					_writer.WriteElementString("Frame", line);

				_writer.WriteEndElement();
			}

			if (ex.InnerException != null)
				EmitError(ex.InnerException, "InnerException", timestamp: false);

			_writer.WriteEndElement();
		}

		public void Close()
		{
			_writer.WriteEndElement();
		}
	}
}
