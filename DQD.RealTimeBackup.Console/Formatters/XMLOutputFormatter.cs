using System;
using System.Xml;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.Bridge.Messages;
using DQD.RealTimeBackup.Scan;
using DQD.RealTimeBackup.Storage;

using Konsole = System.Console;

namespace DQD.RealTimeBackup.Console.Formatters
{
	public class XMLOutputFormatter : IOutputFormatter
	{
		XmlWriter _writer;

		public XMLOutputFormatter()
		{
			_writer = new XmlTextWriter(Konsole.Out);
			_writer.WriteStartElement("RTBOutput");
			_writer.WriteAttributeString("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
		}

		public void EmitGetStatsHeading()
		{
		}

		public void EmitGetStatsResponse(BridgeMessage_GetStats_Response message)
		{
			_writer.WriteStartElement("Statistics");
			_writer.WriteAttributeString("TimestampUTC", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"));

			EmitBackupAgentQueueSizes(message.BackupAgentQueueSizes);

			_writer.WriteElementString("ZFSSnapshotCount", message.ZFSSnapshotCount.ToString());

			_writer.WriteEndElement();
		}

		void EmitBackupAgentQueueSizes(BackupAgentQueueSizes? queueSizes)
		{
			_writer.WriteStartElement("BackupAgentQueueSizes");

			if (queueSizes == null)
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

		public void EmitUploadThreads(UploadStatus?[] uploadThreads)
		{
			_writer.WriteStartElement("UploadThreads");

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

		public void EmitRescanStarted(PerformRescanResponse response)
		{
			_writer.WriteStartElement("RescanRequested");
			_writer.WriteAttributeString("TimestampUTC", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"));
			_writer.WriteAttributeString("RescanNumber", response.RescanNumber.ToString());
			_writer.WriteAttributeString("AlreadyRunning", response.AlreadyRunning ? "true" : "false");
			_writer.WriteEndElement();
		}

		public void EmitNoRescanStatus()
		{
			_writer.WriteStartElement("RescanStatus");
			_writer.WriteAttributeString("xsi:nil", "true");
			_writer.WriteEndElement();
		}

		public void EmitRescanStatusHeadings()
		{
		}

		public void EmitRescanStatus(RescanStatus status)
		{
			_writer.WriteStartElement("Statistics");
			_writer.WriteAttributeString("TimestampUTC", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"));
			_writer.WriteAttributeString("RescanNumber", status.RescanNumber.ToString());
			_writer.WriteAttributeString("IsRunning", status.IsRunning ? "true" : "false");
			_writer.WriteAttributeString("NumberOfFilesLeftToMatch", status.NumberOfFilesLeftToMatch.ToString());

			EmitBackupAgentQueueSizes(status.BackupAgentQueueSizes);

			_writer.WriteElementString("ZFSSnapshotCount", status.ZFSSnapshotCount.ToString());

			_writer.WriteEndElement();
		}

		public void EmitRescanCancelled()
		{
			_writer.WriteStartElement("CancelRescanRequested");
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
