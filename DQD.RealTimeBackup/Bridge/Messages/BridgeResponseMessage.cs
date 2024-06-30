using DQD.RealTimeBackup.Bridge.Serialization;

namespace DQD.RealTimeBackup.Bridge.Messages
{
	public abstract class BridgeResponseMessage : BridgeMessage
	{
		[FieldOrder(1000)]
		public ErrorInfo? Error;
	}
}
