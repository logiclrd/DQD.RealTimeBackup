using System.IO;
using System.Threading;

namespace DeltaQ.RTB.Storage
{
	public interface IRemoteStorage
	{
		public void UploadFile(string serverPath, Stream content, CancellationToken cancellationToken);
		public void MoveFile(string serverPathFrom, string serverPathTo, CancellationToken cancellationToken);
		public void DeleteFile(string serverPath, CancellationToken cancellationToken);
	}
}

