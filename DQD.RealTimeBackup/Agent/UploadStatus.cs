using System.ComponentModel;

using DQD.RealTimeBackup.Bridge.Serialization;
using DQD.RealTimeBackup.Storage;

namespace DQD.RealTimeBackup.Agent
{
	public class UploadStatus
	{
		[FieldOrder(0)]
		public string Path;
		[FieldOrder(1)]
		public long FileSize;
		[FieldOrder(2)]
		public UploadProgress? Progress;

		public bool RecheckAfterUploadCompletes;

		[EditorBrowsable(EditorBrowsableState.Never)]
		public UploadStatus()
		{
			Path = "";
		}

		public UploadStatus(string path)
		{
			Path = path;
		}

		public UploadStatus(string path, long fileSize)
		{
			Path = path;
			FileSize = fileSize;
		}
	}
}
