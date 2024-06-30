namespace DQD.RealTimeBackup.Interop
{
	public interface IMount
	{
		int MountID { get; }
		int ParentMountID { get; }
		int DeviceMajor { get; }
		int DeviceMinor { get; }
		string Root { get; }
		string MountPoint { get; }
		string Options { get; }
		string[] OptionalFields { get; }
		string FileSystemType { get; }
		string DeviceName { get; }
		string? SuperblockOptions { get; }

		bool TestDeviceAccess();
	}
}
