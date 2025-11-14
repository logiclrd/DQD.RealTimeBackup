namespace DQD.RealTimeBackup.FileSystem
{
	public class ZFSVolume
	{
		public string? DeviceName;
		public string? DataSet;
		public long UsedBytes;
		public long AvailableBytes;
		public long ReferencedBytes;
		public string? MountPoint;
		public string? SnapshotName;
	}
}

