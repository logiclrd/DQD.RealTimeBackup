using System;

using DeltaQ.RTB.Bridge.Messages;
using DeltaQ.RTB.Bridge.Serialization;

namespace DeltaQ.RTB.Bridge.Notifications
{
	public class Notification
	{
		[FieldOrder(0)]
		public long MessageID;
		[FieldOrder(1)]
		public DateTime TimestampUTC = DateTime.UtcNow;
		[FieldOrder(2)]
		public string? ErrorMessage;
		[FieldOrder(3)]
		public ErrorInfo? Error;
		[FieldOrder(4)]
		public StateEvent Event;
	}
}
