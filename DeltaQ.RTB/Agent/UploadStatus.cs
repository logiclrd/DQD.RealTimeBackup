using System.ComponentModel;

using DeltaQ.RTB.Bridge.Serialization;
using DeltaQ.RTB.Storage;

namespace DeltaQ.RTB.Agent
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
