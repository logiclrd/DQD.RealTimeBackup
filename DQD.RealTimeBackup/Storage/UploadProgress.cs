using DQD.RealTimeBackup.Bridge.Serialization;

namespace DQD.RealTimeBackup.Storage
{
	public class UploadProgress
	{
		[FieldOrder(0)]
		public long BytesPerSecond;
		[FieldOrder(1)]
		public long BytesTransferred;
	}
}
