using System;
using System.Collections.Generic;
using DeltaQ.RTB.Bridge.Serialization;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public abstract class BridgeResponseMessage : BridgeMessage
	{
		[FieldOrder(1000)]
		public ErrorInfo? Error;
	}
}
