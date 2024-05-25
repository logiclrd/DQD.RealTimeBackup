using System;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bytewizer.Backblaze;
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

		void VerboseWriteLine(object line)
		{
			if (_parameters.Verbose)
				Console.WriteLine(line);
		}

		void VerboseWriteLine(string format, params object?[] args)
		{
			if (_parameters.Verbose)
				Console.WriteLine(format, args);
		}

		object _authenticationSync = new object();
		long _authenticationCount;

		public void Authenticate()
		{
			long authenticationCountOnEntry = _authenticationCount;

			lock (_authenticationSync)
			{
				if (_authenticationCount == authenticationCountOnEntry)
				{
					_b2Client.Connect();
					_authenticationCount++;
				}
			}
		}

		async Task<IApiResults<TResult>> RetryIfNoTomesAreAvailable<TResult>(Func<Task<IApiResults<TResult>>> action)
		{
			while (true)
			{
				var result = await action();

				if ((result.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
				 && (result.HttpResponse.ReasonPhrase != null)
				 && result.HttpResponse.ReasonPhrase.Contains("no tomes available"))
				{
					VerboseWriteLine("[B@] No tomes available, waiting a few seconds and retrying");
					await Task.Delay(TimeSpan.FromSeconds(2));
					continue;
				}

				return result;
			}
		}

		async Task<IApiResults<TResult>> AutomaticallyReauthenticateAsync<TResult>(Func<Task<IApiResults<TResult>>> action)
		{
			var result = await RetryIfNoTomesAreAvailable(action);

			if (result.IsSuccessStatusCode)
				return result;

			if (result.StatusCode == System.Net.HttpStatusCode.Unauthorized)
			{
				Authenticate();

				result = await RetryIfNoTomesAreAvailable(action);

				result.EnsureSuccessStatusCode();

				return result;
			}

			result.EnsureSuccessStatusCode();

			throw new Exception("Error (should not hit this line)");
		}

		// This method is intended for small resources.
		string DownloadFileString(string bucketId, string serverPath, CancellationToken cancellationToken)
		{
			var buffer = new MemoryStream();

			var request = new DownloadFileByNameRequest(bucketId, serverPath);

			var result = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.DownloadAsync(request, buffer, default, cancellationToken)));

			if (!result.IsSuccessStatusCode)
				throw new Exception("The operation did not complete successfully.");

			return Encoding.UTF8.GetString(buffer.ToArray());
		}

		string? DownloadFileStringNoErrorIfNonexistent(string bucketId, string serverPath, CancellationToken cancellationToken)
		{
			var buffer = new MemoryStream();

			var request = new DownloadFileByNameRequest(bucketId, serverPath);

			try
			{
				var result = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.DownloadAsync(request, buffer, default, cancellationToken)));

				if (!result.IsSuccessStatusCode)
				{
					if (result.StatusCode == System.Net.HttpStatusCode.NotFound)
						return null;
					else
						throw new Exception("The operation did not complete successfully.");
				}

				return Encoding.UTF8.GetString(buffer.ToArray());
			}
			catch (AggregateException ex)
			{
				if ((ex.InnerException is ApiException apiException)
			     && (apiException.StatusCode == System.Net.HttpStatusCode.NotFound))
					return null;
				else
					throw;
			}
		}

		// This method is intended for small resources.
		byte[] DownloadFileBytes(string bucketId, string serverPath, CancellationToken cancellationToken)
		{
			var buffer = new MemoryStream();

			var request = new DownloadFileByNameRequest(bucketId, serverPath);

			var result = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.DownloadAsync(request, buffer, default, cancellationToken)));

			if (!result.IsSuccessStatusCode)
				throw new Exception("The operation did not complete successfully.");

			return buffer.ToArray();
		}

		const string Alphabet = "0123456789abcdefghijklmnopqrstuvxyzABCDEFGHIJKLMNOPQRSTUVXYZ";

		public void UploadFileDirect(string serverPath, Stream contentStream, CancellationToken cancellationToken)
		{
			VerboseWriteLine("[B2] Deleting existing file, if any: {0}", serverPath);

			try
			{
				Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, serverPath)));
			}
			catch { }

			VerboseWriteLine("[B2] Uploading file, {0} bytes, directly to path: {1}", contentStream.Length, serverPath);

			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.UploadAsync(
				_parameters.RemoteStorageBucketID,
				serverPath,
				contentStream,
				lastModified: DateTime.UtcNow,
				isReadOnly: false,
				isHidden: false,
				isArchive: true,
				isCompressed: false,
				progress: null,
				cancel: cancellationToken)));

			VerboseWriteLine("[B2] Upload complete");
		}

		public void UploadFile(string serverPath, Stream contentStream, CancellationToken cancellationToken)
		{
			VerboseWriteLine("[B2] Checking for existing file: {0}", serverPath);

			if (DownloadFileStringNoErrorIfNonexistent(_parameters.RemoteStorageBucketID, serverPath, cancellationToken) is string contentKey)
			{
				VerboseWriteLine("[B2] => Existing content key: {0}", contentKey);
				VerboseWriteLine("[B2] => Deleting...");

				Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, contentKey)));
				Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, serverPath)));
			}

			char[] contentKeyChars = new char[128];

			Random rnd = new Random();

			for (int i=0; i < contentKeyChars.Length; i++)
				contentKeyChars[i] = Alphabet[rnd.Next(Alphabet.Length)];

			contentKey = new string(contentKeyChars);

			VerboseWriteLine("[B2] Uploading file to path: {0}", serverPath);
			VerboseWriteLine("[B2] => New content key: {0}", contentKey);
			VerboseWriteLine("[B2] => Uploading {0:#,##0} bytes to content path", contentStream.Length);

			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.UploadAsync(
				_parameters.RemoteStorageBucketID,
				contentKey,
				contentStream,
				lastModified: DateTime.UtcNow,
				isReadOnly: false,
				isHidden: false,
				isArchive: true,
				isCompressed: false,
				progress: null,
				cancel: cancellationToken)));

			VerboseWriteLine("[B2] => Uploading reference to content key to subject path");

			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.UploadAsync(
				_parameters.RemoteStorageBucketID,
				serverPath,
				new MemoryStream(Encoding.UTF8.GetBytes(contentKey)),
				lastModified: DateTime.UtcNow,
				isReadOnly: false,
				isHidden: false,
				isArchive: true,
				isCompressed: false,
				progress: null,
				cancel: cancellationToken)));

			VerboseWriteLine("[B2] Upload complete");
		}

		public void MoveFile(string serverPathFrom, string serverPathTo, CancellationToken cancellationToken)
		{
			var contentKey = DownloadFileBytes(_parameters.RemoteStorageBucketID, serverPathFrom, cancellationToken);

			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.UploadAsync(
				_parameters.RemoteStorageBucketID,
				serverPathTo,
				new MemoryStream(contentKey),
				lastModified: DateTime.UtcNow,
				isReadOnly: false,
				isHidden: false,
				isArchive: true,
				isCompressed: false,
				progress: null,
				cancel: cancellationToken)));

			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, serverPathFrom)));
		}

		public void DeleteFile(string serverPath, CancellationToken cancellationToken)
		{
			var contentKey = DownloadFileString(_parameters.RemoteStorageBucketID, serverPath, cancellationToken);

			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, serverPath)));
			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, contentKey)));
		}
	}
}
