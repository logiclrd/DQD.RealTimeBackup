using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Bytewizer.Backblaze;
using Bytewizer.Backblaze.Client;
using Bytewizer.Backblaze.Models;

using DeltaQ.RTB.Utility;

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

		string? _bucketName;

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

		string FindAndCacheBucketName()
		{
			if (_bucketName == null)
			{
				var request = new ListBucketsRequest(_b2Client.AccountId);

				request.BucketId = _parameters.RemoteStorageBucketID;

				var response = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Buckets.ListAsync(request, TimeSpan.FromSeconds(2))));

				response.EnsureSuccessStatusCode();

				var bucketInfo = response.Response.Buckets.SingleOrDefault(bucket => bucket.BucketId == _parameters.RemoteStorageBucketID);

				if (bucketInfo == null)
					throw new Exception("Configuration error: Unable to resolve bucket name for bucket id " + _parameters.RemoteStorageBucketID);

				_bucketName = bucketInfo.BucketName;
			}

			return _bucketName;
		}

		// This method is intended for small resources.
		string DownloadFileString(string serverPath, CancellationToken cancellationToken)
		{
			var buffer = new MemoryStream();

			var request = new DownloadFileByNameRequest(FindAndCacheBucketName(), serverPath);

			var result = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.DownloadAsync(request, buffer, default, cancellationToken)));

			if (!result.IsSuccessStatusCode)
				throw new Exception("The operation did not complete successfully.");

			return Encoding.UTF8.GetString(buffer.ToArray());
		}

		string? DownloadFileStringNoErrorIfNonexistent(string serverPath, CancellationToken cancellationToken)
		{
			var buffer = new MemoryStream();

			var request = new DownloadFileByNameRequest(FindAndCacheBucketName(), serverPath);

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

			var request = new DownloadFileByNameRequest(FindAndCacheBucketName(), serverPath);

			var result = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.DownloadAsync(request, buffer, default, cancellationToken)));

			if (!result.IsSuccessStatusCode)
				throw new Exception("The operation did not complete successfully.");

			return buffer.ToArray();
		}

		class UploadProgressProxy : IProgress<ICopyProgress>
		{
			Action<UploadProgress> _progressCallback;

			public UploadProgressProxy(Action<UploadProgress> progressCallback)
			{
				_progressCallback = progressCallback;
			}

			public void Report(ICopyProgress value)
			{
				var uploadProgress = new UploadProgress();

				uploadProgress.BytesPerSecond = value.BytesPerSecond;
				uploadProgress.BytesTransferred = value.BytesTransferred;

				_progressCallback(uploadProgress);
			}
		}

		public void UploadFileInChunks(string serverPath, Stream contentStream, Action<UploadProgress>? progressCallback, CancellationToken cancellationToken)
		{
			var startRequest = new StartLargeFileRequest(
				_parameters.RemoteStorageBucketID,
				serverPath);

			var startResponse = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Parts.StartLargeFileAsync(startRequest)));

			startResponse.EnsureSuccessStatusCode();

			var getUploadPartURLResponse = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Parts.GetUploadUrlAsync(startResponse.Response.FileId)));

			getUploadPartURLResponse.EnsureSuccessStatusCode();

			var uploadAuthorizationToken = getUploadPartURLResponse.Response.AuthorizationToken;
			var fileID = getUploadPartURLResponse.Response.FileId;
			var uploadPartURL = getUploadPartURLResponse.Response.UploadUrl;

			long offset = 0;
			byte[] buffer = new byte[_parameters.B2LargeFileChunkSize];
			int partNumber = 1;

			var partChecksums = new List<string>();

			while (offset < contentStream.Length)
			{
				long remainingBytes = contentStream.Length - offset;

				int chunkLength = (int)Math.Min(remainingBytes, buffer.Length);

				FileUtility.ReadFully(contentStream, buffer, chunkLength, cancellationToken);

				var chunkBufferStream = new MemoryStream(buffer);

				if (chunkLength < chunkBufferStream.Length)
					chunkBufferStream.SetLength(chunkLength);

				// Chunks just sometimes randomly fail. Give 'em another go instead of giving up immediately.
				IApiResults<UploadPartResponse>? partResponse = null;

				for (int i = 0; i < 3; i++)
				{
					try
					{
						chunkBufferStream.Position = 0;

						partResponse = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Parts.UploadAsync(
							uploadPartURL,
							partNumber,
							uploadAuthorizationToken,
							chunkBufferStream,
							progress: progressCallback == null ? default : new UploadProgressProxy(
								progress =>
								{
									progress.BytesTransferred += offset;
									progressCallback(progress);
								}))));

						if (partResponse.IsSuccessStatusCode)
							break;
					}
					catch {}
				}

				// I don't think this ever actually happens, but it satisfies the compiler's static analysis.
				if (partResponse == null)
					throw new Exception("Error uploading chunk: did not get an UploadPartResponse");

				partResponse.EnsureSuccessStatusCode();

				partChecksums.Add(partResponse.Response.ContentSha1);

				offset += chunkLength;
				partNumber++;
			}

			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Parts.FinishLargeFileAsync(startResponse.Response.FileId, partChecksums)));
		}

		void UploadFileImplementation(string serverPath, Stream contentStream, Action<UploadProgress>? progressCallback, CancellationToken cancellationToken)
		{
			if (contentStream.Length > _parameters.B2LargeFileThreshold)
				UploadFileInChunks(serverPath, contentStream, progressCallback, cancellationToken);
			else
			{
				Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.UploadAsync(
					_parameters.RemoteStorageBucketID,
					serverPath,
					contentStream,
					lastModified: DateTime.UtcNow,
					isReadOnly: false,
					isHidden: false,
					isArchive: true,
					isCompressed: false,
					progress: progressCallback == null ? default : new UploadProgressProxy(progressCallback),
					cancel: cancellationToken)));
			}
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

			UploadFileImplementation(
				serverPath,
				contentStream,
				progressCallback: null,
				cancellationToken);

			VerboseWriteLine("[B2] Upload complete");
		}

		string GetFileIdByName(string serverPath)
		{
			VerboseWriteLine("[B2] Resolving file id for name: {0}", serverPath);

			var request = new ListFileVersionRequest(_parameters.RemoteStorageBucketID);

			request.StartFileName = serverPath;
			request.MaxFileCount = 1;

			var response = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.ListVersionsAsync(request, cacheTTL: TimeSpan.FromSeconds(10))));

			if (!response.IsSuccessStatusCode)
				VerboseWriteLine("[B2] => response status: {0}", response.StatusCode);

			response.EnsureSuccessStatusCode();

			var fileId = response.Response.Files.Single(file => file.FileName == serverPath).FileId;

			VerboseWriteLine("[B2] => file id: {0}", fileId);

			return fileId;
		}

		public void DeleteFileDirect(string serverPath, CancellationToken cancellationToken)
		{
			string fileId = GetFileIdByName(serverPath);

			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(fileId, serverPath)));
		}

		public void UploadFile(string serverPath, Stream contentStream, Action<UploadProgress>? progressCallback, CancellationToken cancellationToken)
		{
			VerboseWriteLine("[B2] Checking for existing file: {0}", serverPath);

			if (DownloadFileStringNoErrorIfNonexistent(serverPath, cancellationToken) is string contentKey)
			{
				VerboseWriteLine("[B2] => Existing content key: {0}", contentKey);
				VerboseWriteLine("[B2] => Deleting...");

				DeleteFileDirect(serverPath, cancellationToken);
				DeleteFileDirect(contentKey, cancellationToken);
			}

			char[] contentKeyChars = new char[128];

			Random rnd = new Random();

			for (int i=0; i < contentKeyChars.Length; i++)
				contentKeyChars[i] = Alphabet[rnd.Next(Alphabet.Length)];

			contentKey = new string(contentKeyChars);

			VerboseWriteLine("[B2] Uploading file to path: {0}", serverPath);
			VerboseWriteLine("[B2] => New content key: {0}", contentKey);
			VerboseWriteLine("[B2] => Uploading {0:#,##0} bytes to content path", contentStream.Length);

			UploadFileImplementation(
				contentKey,
				contentStream,
				progressCallback,
				cancellationToken);

			VerboseWriteLine("[B2] => Uploading reference to content key to subject path");

			UploadFileImplementation(
				serverPath,
				new MemoryStream(Encoding.UTF8.GetBytes(contentKey)),
				progressCallback: null,
				cancellationToken);

			VerboseWriteLine("[B2] Upload complete");
		}

		public void MoveFile(string serverPathFrom, string serverPathTo, CancellationToken cancellationToken)
		{
			var contentKey = DownloadFileBytes(_parameters.RemoteStorageBucketID, serverPathFrom, cancellationToken);

			UploadFileImplementation(
				serverPathTo,
				new MemoryStream(contentKey),
				progressCallback: null,
				cancellationToken);

			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(_parameters.RemoteStorageBucketID, serverPathFrom)));
		}

		public void DeleteFile(string serverPath, CancellationToken cancellationToken)
		{
			var contentKey = DownloadFileString(serverPath, cancellationToken);

			DeleteFileDirect(serverPath, cancellationToken);
			DeleteFileDirect(contentKey, cancellationToken);
		}
	}
}
