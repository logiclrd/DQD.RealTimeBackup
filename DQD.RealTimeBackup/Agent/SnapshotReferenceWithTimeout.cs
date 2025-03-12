using System;

using DQD.RealTimeBackup.FileSystem;

namespace DQD.RealTimeBackup.Agent;

public class SnapshotReferenceWithTimeout
{
	public SnapshotReference SnapshotReference;
	public DateTime TimeoutUTC;

	public SnapshotReferenceWithTimeout(SnapshotReference snapshotReference)
	{
		this.SnapshotReference = snapshotReference;
	}

	public override string ToString()
	{
		return SnapshotReference.ToString() + " / Timeout: " + TimeoutUTC.ToString("yyyy-MM-dd HH:mm:ss.fff") + " (UTC)";
	}
}
