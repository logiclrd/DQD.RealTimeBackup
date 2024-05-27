using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Storage
{
	public interface IRemoteStorage : IDiagnosticOutput
	{
		public void Authenticate();

		public void UploadFileDirect(string serverPath, Stream content, CancellationToken cancellationToken);
		public void DownloadFileDirect(string serverPath, Stream content, CancellationToken cancellationToken);
		public void DeleteFileDirect(string serverPath, CancellationToken cancellationToken);
		public void UploadFile(string serverPath, Stream content, Action<UploadProgress>? progressCallback, CancellationToken cancellationToken);
		public void DownloadFile(string serverPath, Stream content, CancellationToken cancellationToken);
		public void MoveFile(string serverPathFrom, string serverPathTo, CancellationToken cancellationToken);
		public void DeleteFile(string serverPath, CancellationToken cancellationToken);

		public IEnumerable<RemoteFileInfo> EnumerateFiles(string pathPrefix, bool recursive);
	}
}

