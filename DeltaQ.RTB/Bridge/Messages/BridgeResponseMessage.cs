using DeltaQ.RTB.Bridge.Serialization;

namespace DeltaQ.RTB.Bridge.Messages
{
	public abstract class BridgeResponseMessage : BridgeMessage
	{
		[FieldOrder(1000)]
		public ErrorInfo? Error;
	}
}
