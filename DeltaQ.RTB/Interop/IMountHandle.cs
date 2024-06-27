namespace DeltaQ.RTB.Interop
{
	public interface IMountHandle
	{
		int FileDescriptor { get; }
		long FileSystemID { get; }
	}
}
