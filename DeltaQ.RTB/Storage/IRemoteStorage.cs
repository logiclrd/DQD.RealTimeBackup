using System.IO;

namespace DeltaQ.RTB.Storage
{
	public interface IRemoteStorage
	{
		public void UploadFile(string serverPath, Stream content);
		public void MoveFile(string serverPathFrom, string serverPathTo);
		public void DeleteFile(string serverPath);
	}
}

