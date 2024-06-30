using System;
using System.Text;

namespace DQD.RealTimeBackup.Interop
{
	public class OpenHandle : IOpenHandle, IDisposable
	{
		int _fd;
		StringBuilder _pathNameBuffer;

		public OpenHandle(int fd, StringBuilder? pathNameBuffer = null)
		{
			if (fd < 0)
				throw new Exception("Invalid file descriptor");

			_fd = fd;
			_pathNameBuffer = pathNameBuffer ?? new StringBuilder();
		}

		public string ReadLink()
		{
			string linkPath = "/proc/self/fd/" + _fd;

			_pathNameBuffer.Length = NativeMethods.MAX_PATH;

			var len = NativeMethods.readlink(linkPath, _pathNameBuffer, _pathNameBuffer.Length);

			if (len < 0)
				throw new Exception("Unable to read link for file opened from another process");

			return _pathNameBuffer.ToString(0, (int)len);
		}

		public void Dispose()
		{
			NativeMethods.close(_fd);
		}
	}
}

