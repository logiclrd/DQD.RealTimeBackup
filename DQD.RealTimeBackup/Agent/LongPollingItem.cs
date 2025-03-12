using System;

using DQD.RealTimeBackup.FileSystem;

namespace DQD.RealTimeBackup.Agent;

public class LongPollingItem
{
	public SnapshotReference CurrentSnapshotReference;
	public DateTime DeadlineUTC; // Upload anyway after this has elapsed.

	public LongPollingItem(SnapshotReference snapshotReference, TimeSpan timeout)
	{
		this.CurrentSnapshotReference = snapshotReference;
		this.DeadlineUTC = DateTime.UtcNow + timeout;
	}

	public override string ToString()
	{
		return CurrentSnapshotReference.ToString() + " / Deadline: " + DeadlineUTC.ToString("yyyy-MM-dd HH:mm:ss.fff") + " (UTC)";
	}
}

