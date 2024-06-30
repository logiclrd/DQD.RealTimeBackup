namespace DQD.RealTimeBackup.Interop
{
	public interface IMountHandle
	{
		int FileDescriptor { get; }
		long FileSystemID { get; }
	}
}
