namespace DQD.RealTimeBackup.Interop
{
	public class OpenByHandleAt : IOpenByHandleAt
	{
		public IOpenHandle? Open(int mountFileDescriptor, byte[] fileHandle)
		{
			int fd = NativeMethods.open_by_handle_at(
				mountFileDescriptor,
				fileHandle,
				NativeMethods.O_RDONLY | NativeMethods.O_NONBLOCK | NativeMethods.O_LARGEFILE | NativeMethods.O_PATH);

			if (fd >= 0)
				return new OpenHandle(fd);
			else
				return null;
		}
	}
}

