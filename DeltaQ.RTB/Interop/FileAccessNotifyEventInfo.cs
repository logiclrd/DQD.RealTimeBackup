namespace DeltaQ.RTB.Interop
{
	public class FileAccessNotifyEventInfo
	{
		public FileAccessNotifyEventInfoType Type;
		public long FileSystemID;
		public byte[]? FileHandle;
		public string? FileName;
	}
}
