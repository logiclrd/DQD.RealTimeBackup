using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Storage
{
	public interface IRemoteStorage : IDiagnosticOutput
	{
		void Authenticate();

		void UploadFileDirect(string serverPath, Stream content, CancellationToken cancellationToken);
		void DownloadFileDirect(string serverPath, Stream content, CancellationToken cancellationToken);
		void DeleteFileDirect(string serverPath, CancellationToken cancellationToken);
		void UploadFile(string serverPath, Stream content, Action<UploadProgress>? progressCallback, CancellationToken cancellationToken);
		void DownloadFile(string serverPath, Stream content, CancellationToken cancellationToken);
		void MoveFile(string serverPathFrom, string serverPathTo, CancellationToken cancellationToken);
		void DeleteFile(string serverPath, CancellationToken cancellationToken);

		IEnumerable<RemoteFileInfo> EnumerateFiles(string pathPrefix, bool recursive);
		IEnumerable<RemoteFileInfo> EnumerateUnfinishedFiles();
	}
}

