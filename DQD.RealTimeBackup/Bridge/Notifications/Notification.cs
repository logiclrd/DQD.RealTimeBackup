using System;

using DQD.RealTimeBackup.Bridge.Messages;
using DQD.RealTimeBackup.Bridge.Serialization;

namespace DQD.RealTimeBackup.Bridge.Notifications
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
		public string? Summary;
		[FieldOrder(4)]
		public ErrorInfo? Error;
		[FieldOrder(5)]
		public StateEvent Event;
	}
}
