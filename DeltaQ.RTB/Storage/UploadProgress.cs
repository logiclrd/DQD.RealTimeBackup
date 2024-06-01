using DeltaQ.RTB.Bridge.Serialization;

namespace DeltaQ.RTB.Storage
{
	public class UploadProgress
	{
		[FieldOrder(0)]
		public long BytesPerSecond;
		[FieldOrder(1)]
		public long BytesTransferred;
	}
}
