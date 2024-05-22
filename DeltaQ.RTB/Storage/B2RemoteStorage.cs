using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Bytewizer.Backblaze.Client;
using Bytewizer.Backblaze.Models;

namespace DeltaQ.RTB.Storage
{
	public class B2RemoteStorage : IRemoteStorage
	{
		// B2 does not support renaming or moving files. Therefore, in order to make this possible with our
		// data model, we upload the actual file content to a unique path, which in this implementation is
		// a 128-character hex string, and then we upload the hex string to the actual server path. Later,
		// downloading the file content requires following the indirection. Moving the file is just a
		// matter of uploading a new link to the key, and then deleting the old link to the key.

		OperatingParameters _parameters;
		IStorageClient _b2Client;

		static void Wait(Task task)
		{
			task.ConfigureAwait(false);
			task.Wait();
		}

		static TResult Wait<TResult>(Task<TResult> task)
		{
			task.ConfigureAwait(false);
			task.Wait();

			return task.Result;
		}

		public B2RemoteStorage(OperatingParameters parameters, IStorageClient b2Client)
		{
			_parameters = parameters;
			_b2Client = b2Client;
		}

		// This method is intended for small resources.
		string DownloadFileString(string bucketId, string serverPath, CancellationToken cancellationToken)
		{
			var buffer = new MemoryStream();

			var request = new DownloadFileByNameRequest(bucketId, serverPath);

			var result = Wait(_b2Client.DownloadAsync(request, buffer, default, cancellationToken));

			if (!result.IsSuccessStatusCode)
				throw new Exception("The operation did not complete successfully.");

			return Encoding.UTF8.GetString(buffer.ToArray());
		}

		// This method is intended for small resources.
		byte[] DownloadFileBytes(string bucketId, string serverPath, CancellationToken cancellationToken)
		{
			var buffer = new MemoryStream();

			var request = new DownloadFileByNameRequest(bucketId, serverPath);

			var result = Wait(_b2Client.DownloadAsync(request, buffer, default, cancellationToken));

			if (!result.IsSuccessStatusCode)
				throw new Exception("The operation did not complete successfully.");

			return buffer.ToArray();
		}

		const string Alphabet = "0123456789abcdefghijklmnopqrstuvxyzABCDEFGHIJKLMNOPQRSTUVXYZ";

		public void UploadFile(string serverPath, Stream contentStream, CancellationToken cancellationToken)
		{
			if (DownloadFileString(_parameters.RemoteStorageBucketID, serverPath, cancellationToken) is string contentKey)
				Wait(_b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, contentKey));

			char[] contentKeyChars = new char[128];

			Random rnd = new Random();

			for (int i=0; i < contentKeyChars.Length; i++)
				contentKeyChars[i] = Alphabet[rnd.Next(Alphabet.Length)];

			contentKey = new string(contentKeyChars);

			Wait(_b2Client.Files.UploadAsync(
				_parameters.RemoteStorageBucketID,
				contentKey,
				contentStream,
				lastModified: DateTime.UtcNow,
				isReadOnly: false,
				isHidden: false,
				isArchive: true,
				isCompressed: false,
				progress: null,
				cancel: cancellationToken));

			Wait(_b2Client.Files.UploadAsync(
				_parameters.RemoteStorageBucketID,
				serverPath,
				contentStream,
				lastModified: DateTime.UtcNow,
				isReadOnly: false,
				isHidden: false,
				isArchive: true,
				isCompressed: false,
				progress: null,
				cancel: cancellationToken));
		}

		public void MoveFile(string serverPathFrom, string serverPathTo, CancellationToken cancellationToken)
		{
			var contentKey = DownloadFileBytes(_parameters.RemoteStorageBucketID, serverPathFrom, cancellationToken);

			Wait(_b2Client.Files.UploadAsync(
				_parameters.RemoteStorageBucketID,
				serverPathTo,
				new MemoryStream(contentKey),
				lastModified: DateTime.UtcNow,
				isReadOnly: false,
				isHidden: false,
				isArchive: true,
				isCompressed: false,
				progress: null,
				cancel: cancellationToken));

			Wait(_b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, serverPathFrom));
		}

		public void DeleteFile(string serverPath, CancellationToken cancellationToken)
		{
			var contentKey = DownloadFileString(_parameters.RemoteStorageBucketID, serverPath, cancellationToken);

			Wait(_b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, contentKey));
			Wait(_b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, serverPath));
		}
	}
}
