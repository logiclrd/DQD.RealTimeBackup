using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.Utility;

using Bytewizer.Backblaze;
using Bytewizer.Backblaze.Client;
using Bytewizer.Backblaze.Models;
using Bytewizer.Backblaze.Utility;

using HttpStatusCode = System.Net.HttpStatusCode;

namespace DQD.RealTimeBackup.Storage
{
	public class B2RemoteStorage : DiagnosticOutputBase, IRemoteStorage
	{
		// B2 does not support renaming or moving files. Therefore, in order to make this possible with our
		// data model, we upload the actual file content to a unique path, which in this implementation is
		// a 128-character hex string, and then we upload the hex string to the actual server path. Later,
		// downloading the file content requires following the indirection. Moving the file is just a
		// matter of uploading a new link to the key, and then deleting the old link to the key.

		OperatingParameters _parameters;
		IContentKeyGenerator _contentKeyGenerator;
		IStorageClient _b2Client;
		IErrorLogger _errorLogger;

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

		public B2RemoteStorage(OperatingParameters parameters, IContentKeyGenerator contentKeyGenerator, IStorageClient b2Client, IErrorLogger errorLogger)
		{
			_parameters = parameters;
			_contentKeyGenerator = contentKeyGenerator;
			_b2Client = b2Client;
			_errorLogger = errorLogger;
		}

		object _authenticationSync = new object();
		long _authenticationCount;

		long GetAuthenticationState() => _authenticationCount;

		public void Authenticate()
			=> Authenticate(GetAuthenticationState());

		void Authenticate(long previousAuthenticationState)
		{
			lock (_authenticationSync)
			{
				if (_authenticationCount == previousAuthenticationState)
				{
					_b2Client.Connect();
					_authenticationCount++;
				}
			}
		}

		async Task<IApiResults<TResult>> RetryIfNoTomesAreAvailable<TResult>(Func<Task<IApiResults<TResult>>> action)
		{
			int nullResponseCount = 0;

			while (true)
			{
				var result = await action();

				if (result == null)
				{
					nullResponseCount++;

					if (nullResponseCount >= 3)
						throw new NullResponseException("Internal error: Received null response from action functor");

					await Task.Delay(TimeSpan.FromSeconds(1));
					continue;
				}

				if ((result.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
				 && (result.HttpResponse != null)
				 && (result.HttpResponse.ReasonPhrase != null)
				 && result.HttpResponse.ReasonPhrase.Contains("no tomes available"))
				{
					VerboseDiagnosticOutput("[B2] No tomes available, waiting a few seconds and retrying");
					await Task.Delay(TimeSpan.FromSeconds(2));
					continue;
				}

				return result;
			}
		}

		async Task<IApiResults<TResult>> AutomaticallyReauthenticateAsync<TResult>(Func<Task<IApiResults<TResult>>> action)
		{
			long authenticationStateOnEntry = GetAuthenticationState();

			var result = await RetryIfNoTomesAreAvailable(action);

			if (result.IsSuccessStatusCode)
				return result;

			if (result.StatusCode == System.Net.HttpStatusCode.Unauthorized)
			{
				const int MaxAuthenticateRetries = 3;

				for (int retry = 1; retry <= MaxAuthenticateRetries; retry++)
				{
					bool haveRetries = (retry < MaxAuthenticateRetries);

					try
					{
						Authenticate(authenticationStateOnEntry);
						break;
					}
					catch when (haveRetries)
					{
					}
				}

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
				{
					throw _errorLogger
						.LogError("Configuration error: Unable to resolve bucket name for bucket id " + _parameters.RemoteStorageBucketID, ErrorLogger.Summary.ConfigurationError)
						.ToException();
				}

				_bucketName = bucketInfo.BucketName;
			}

			return _bucketName;
		}

		static char[] B2ProblematicFileNameCharacters = { ',', '[', ']', '&', '_' };

		// This method is intended for small resources.
		string? DownloadFileString(string serverPath, CancellationToken cancellationToken)
		{
			// BUGS IN BACKBLAZE B2 API: Not all supported filenames work with the b2_download_file_by_name
			// endpoint.
			//
			// - Filenames that contain commas confuse the URL decoder, producing an error:
			//
			//   "Bad character in percent-encoded string: 44 (0x2C)",
			//
			// - Filenames that contain square brackets apparently trip up an overzealous security filter:
			//
			//   "the server cannot or will not process the request due to something that is perceived to
			//   be a client error eg malformed request syntax invalid request message framing or deceptive
			//   request routing)"
			//
			// A workaround has been identified: These files work just file with the b2_list_file_versions
			// endpoint, which is what underlies GetFileIDByName, and downloading the files by ID bypasses
			// any nonsense about the filenames. However, this is less efficient because two requests are
			// needed. As such, we want to use DownloadFileByNameRequest where we can.
			//
			// These characters are known to trigger problems presently:
			// - ,
			// - [
			// - ]
			//
			// Any filenames containing these will require the work-around. If the filename does not have
			// any offending characters in it, then a straightforward by-name download can be done.

			var buffer = new MemoryStream();

			Func<Task<IApiResults<DownloadFileResponse>>> functor;

			if (serverPath.IndexOfAny(B2ProblematicFileNameCharacters) < 0)
			{
				// Fast path: b2_download_file_by_name
				var request = new DownloadFileByNameRequest(FindAndCacheBucketName(), serverPath);

				functor = () => _b2Client.DownloadAsync(request, buffer, default, cancellationToken);
			}
			else
			{
				// Workaround: b2_list_file_versions -> b2_download_file_by_id
				var fileID = GetFileIDByName(serverPath);

				var request = new DownloadFileByIdRequest(fileID);

				functor = () => _b2Client.DownloadByIdAsync(request, buffer, default, cancellationToken);
			}

			const int MaxDownloadAttempts = 5;

			for (int retry = 1; retry <= MaxDownloadAttempts; retry++)
			{
				bool haveRetries = (retry < MaxDownloadAttempts);

				try
				{
					buffer.Position = 0;
					buffer.SetLength(0);

					var result = Wait(AutomaticallyReauthenticateAsync(functor));

					if (!result.IsSuccessStatusCode)
						throw new Exception("The operation did not complete successfully.");

					return Encoding.UTF8.GetString(buffer.ToArray());
				}
				catch (Exception ex) when (IsNotFoundException(ex))
				{
					return null;
				}
				catch when (haveRetries)
				{
				}
				catch (Exception ex)
				{
					throw new Exception("Failed to download file after " + MaxDownloadAttempts + " attempts: " + serverPath, ex);
				}
			}

			throw new Exception("Failed to download file after " + MaxDownloadAttempts + " attempts: " + serverPath);
		}

		string? DownloadFileStringNoErrorIfNonexistent(string serverPath, CancellationToken cancellationToken)
		{
			// BUGS IN BACKBLAZE B2 API: See discussion at top of DownloadFileString.

			var buffer = new MemoryStream();

			Func<Task<IApiResults<DownloadFileResponse>>> functor;

			if (serverPath.IndexOfAny(B2ProblematicFileNameCharacters) < 0)
			{
				// Fast path: b2_download_file_by_name
				var request = new DownloadFileByNameRequest(FindAndCacheBucketName(), serverPath);

				functor = () => _b2Client.DownloadAsync(request, buffer, default, cancellationToken);
			}
			else
			{
				// Workaround: b2_list_file_versions -> b2_download_file_by_id
				var fileID = GetFileIDByName(serverPath, throwIfNotFound: false);

				if (fileID == null)
					return null;

				var request = new DownloadFileByIdRequest(fileID);

				functor = () => _b2Client.DownloadByIdAsync(request, buffer, default, cancellationToken);
			}

			const int MaxDownloadRetries = 3;

			for (int retry = 1; retry <= MaxDownloadRetries; retry++)
			{
				bool haveRetries = (retry < MaxDownloadRetries);

				try
				{
					var result = Wait(AutomaticallyReauthenticateAsync(functor));

					if (!result.IsSuccessStatusCode)
					{
						if (result.StatusCode == System.Net.HttpStatusCode.NotFound)
							return null;
						else
							continue;
					}

					return Encoding.UTF8.GetString(buffer.ToArray());
				}
				catch (AggregateException ex)
				{
					if ((ex.InnerException is ApiException apiException)
					 && (apiException.StatusCode == System.Net.HttpStatusCode.NotFound))
						return null;
					else if (!haveRetries || cancellationToken.IsCancellationRequested)
						throw;
				}
			}

			throw new Exception("The operation did not complete successfully.");
		}

		// This method is intended for small resources. This method does not support files in parts.
		byte[] DownloadFileBytes(string serverPath, CancellationToken cancellationToken)
		{
			// BUGS IN BACKBLAZE B2 API: See discussion at top of DownloadFileString.

			var buffer = new MemoryStream();

			Func<Task<IApiResults<DownloadFileResponse>>> functor;

			if (serverPath.IndexOfAny(B2ProblematicFileNameCharacters) < 0)
			{
				// Fast path: b2_download_file_by_name
				var request = new DownloadFileByNameRequest(FindAndCacheBucketName(), serverPath);

				functor = () => _b2Client.DownloadAsync(request, buffer, default, cancellationToken);
			}
			else
			{
				// Workaround: b2_list_file_versions -> b2_download_file_by_id
				var fileID = GetFileIDByName(serverPath);

				var request = new DownloadFileByIdRequest(fileID);

				functor = () => _b2Client.DownloadByIdAsync(request, buffer, default, cancellationToken);
			}

			var result = Wait(AutomaticallyReauthenticateAsync(functor));

			if (!result.IsSuccessStatusCode)
				throw new Exception("The operation did not complete successfully.");

			return buffer.ToArray();
		}

		class UploadProgressProxy : IProgress<ICopyProgress>
		{
			Action<UploadProgress> _progressCallback;

			SpeedCalculator _speedCalculator;

			public UploadProgressProxy(Action<UploadProgress> progressCallback)
			{
				_progressCallback = progressCallback;

				_speedCalculator = new SpeedCalculator();
			}

			public void Report(ICopyProgress value)
			{
				var uploadProgress = new UploadProgress();

				// Calculate the speed ourselves, so that we can bridge over the boundary between chunks in
				// large uploads. Otherwise, right at the start of chunks there are sometimes sudden jumps
				// to implausibly high speeds, just for a second or so.
				_speedCalculator.AddSample(value.BytesTransferred);

				uploadProgress.BytesPerSecond = _speedCalculator.CalculateBytesPerSecond();
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

			try
			{
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
					Exception? lastException = null;

					const int MaxChunkAttempts = 3;

					for (int i = 0; i < MaxChunkAttempts; i++)
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
						catch (Exception e)
						{
							lastException = e;
						}
					}

					if (partResponse == null)
						throw new Exception("Error uploading chunk #" + partNumber + " at offset " + offset + " after " + MaxChunkAttempts + " attempts. Path: " + serverPath, lastException);

					partResponse.EnsureSuccessStatusCode();

					partChecksums.Add(partResponse.Response.ContentSha1);

					offset += chunkLength;
					partNumber++;
				}

				const int MaxFinishRetries = 3;

				for (int retry = 1; retry <= MaxFinishRetries; retry++)
				{
					bool haveRetries = (retry < MaxFinishRetries);

					try
					{
						Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Parts.FinishLargeFileAsync(startResponse.Response.FileId, partChecksums)));
						break;
					}
					catch when (haveRetries)
					{
					}
					catch (Exception ex)
					{
						throw new Exception("Error completing file after " + (partNumber - 1) + " parts uploaded. Attempted finish call " + MaxFinishRetries + " times. Path: " + serverPath, ex);
					}
				}
			}
			catch
			{
				try
				{
					Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Parts.CancelLargeFileAsync(startResponse.Response.FileId)));
				}
				catch {}

				throw;
			}
		}

		void UploadFileImplementation(string serverPath, Stream contentStream, Action<UploadProgress>? progressCallback, CancellationToken cancellationToken)
		{
			if (contentStream.Length > _parameters.B2LargeFileThreshold)
				UploadFileInChunks(serverPath, contentStream, progressCallback, cancellationToken);
			else
			{
				const int MaxUploadAttempts = 5;

				var startingPosition = contentStream.Position;

				for (int retry = 1; retry <= MaxUploadAttempts; retry++)
				{
					bool haveRetries = (retry < MaxUploadAttempts);

					try
					{
						contentStream.Position = startingPosition;

						var uploadResponse = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.UploadAsync(
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

						uploadResponse.EnsureSuccessStatusCode();

						break;
					}
					catch when (haveRetries && contentStream.CanSeek && !cancellationToken.IsCancellationRequested)
					{
					}
					catch (Exception e)
					{
						if (!contentStream.CanSeek)
							throw new Exception("Can't retry upload because the content stream is not seekable", e);
						if (cancellationToken.IsCancellationRequested)
							throw new TaskCanceledException("Upload cancelled", e);
						if (!haveRetries)
							throw new Exception("Upload task failed after " + MaxUploadAttempts + " retries", e);

						throw new Exception("Unknown failure of upload task", e);
					}
				}
			}
		}

		public void UploadFileDirect(string serverPath, Stream contentStream, CancellationToken cancellationToken)
		{
			VerboseDiagnosticOutput("[B2] Deleting existing file, if any: {0}", serverPath);

			try
			{
				Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(GetFileIDByName(serverPath), serverPath)));
			}
			catch { }

			VerboseDiagnosticOutput("[B2] Uploading file, {0} bytes, directly to path: {1}", contentStream.Length, serverPath);

			UploadFileImplementation(
				serverPath,
				contentStream,
				progressCallback: null,
				cancellationToken);

			VerboseDiagnosticOutput("[B2] Upload complete");
		}

		string? GetFileIDByName(string serverPath, bool throwIfNotFound = true)
		{
			VerboseDiagnosticOutput("[B2] Resolving file id for name: {0}", serverPath);

			var request = new ListFileVersionRequest(_parameters.RemoteStorageBucketID);

			request.StartFileName = serverPath;
			request.MaxFileCount = 1;

			var response = Wait(AutomaticallyReauthenticateAsync(
				() =>
				{
					const int MaxRetries = 3;

					for (int retry = 1; retry <= MaxRetries; retry++)
					{
						bool haveRetries = (retry < MaxRetries);

						try
						{
							var result = _b2Client.Files.ListVersionsAsync(request, cacheTTL: TimeSpan.FromSeconds(10));

							if (result != null)
								return result;
						}
						catch when (haveRetries)
						{
							Thread.Sleep(500);
							continue;
						}
						catch (Exception e)
						{
							_errorLogger.LogError(
								"Exception calling B2 Client ListVersionsAsync",
								"The call to B2Client.Files.ListVersionsAsync threw an exception. Retries have been exhausted.",
								exception: e);

							throw;
						}

						_errorLogger.LogError(
							"B2 Client ListVersionsAsync returned null",
							"The call to B2Client.Files.ListVersionsAsync returned null. This is not supposed to happen and probably " +
							"indicates a bug in the B2 library. Retrying (" + retry + " / " + MaxRetries);
					}

					throw new Exception("ListVersionsAsync returns null for path " + serverPath);
				}));

			if (!response.IsSuccessStatusCode)
				VerboseDiagnosticOutput("[B2] => response status: {0}", response.StatusCode);

			response.EnsureSuccessStatusCode();

			var file = response.Response.Files.SingleOrDefault(file => file.FileName == serverPath);

			if (file == null)
			{
				VerboseDiagnosticOutput("[B2] => file was not found");

				if (throwIfNotFound)
					throw new FileNotFoundException();
				else
					return null;
			}

			var fileID = file.FileId;

			VerboseDiagnosticOutput("[B2] => file id: {0}", fileID);

			return fileID;
		}

		public bool DeleteFileDirect(string serverPath, CancellationToken cancellationToken)
		{
			if (!(GetFileIDByName(serverPath, throwIfNotFound: false) is string fileID))
				return false;
			else
			{
				Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(fileID, serverPath)));
				return true;
			}
		}

		public void DeleteFileByID(string remoteFileID, string serverPath, CancellationToken cancellationToken)
		{
			Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(remoteFileID, serverPath)));
		}

		bool IsNotFoundException(Exception e)
		{
			if (e is FileNotFoundException)
				return true;

			if (e is ApiException apiException)
			{
				if (apiException.StatusCode == HttpStatusCode.NotFound)
					return true;

				if ((apiException.StatusCode == HttpStatusCode.BadRequest)
				 && (apiException.Error.Code == "file_not_present"))
				  return true;
			}

			if (e is AggregateException aggregateException)
				return aggregateException.InnerExceptions.Any(IsNotFoundException);

			if (e.InnerException != null)
				return IsNotFoundException(e.InnerException);

			return false;
		}

		public void UploadFile(string serverPath, Stream contentStream, out string newContentKey, Action<UploadProgress>? progressCallback, CancellationToken cancellationToken)
		{
			VerboseDiagnosticOutput("[B2] Checking for existing file: {0}", serverPath);

			if (DownloadFileStringNoErrorIfNonexistent(serverPath, cancellationToken) is string contentKey)
			{
				VerboseDiagnosticOutput("[B2] => Existing content key: {0}", contentKey);
				VerboseDiagnosticOutput("[B2] => Deleting...");

				try
				{
					DeleteFileDirect(serverPath, cancellationToken);
				}
				catch (Exception e)
				{
					if (!IsNotFoundException(e))
						_errorLogger.LogError("Error while deleting path from which a content key was just retrieved: " + serverPath, ErrorLogger.Summary.SystemError, e);
				}

				try
				{
					DeleteFileDirect(contentKey, cancellationToken);
				}
				catch (Exception e)
				{
					if (!IsNotFoundException(e))
						_errorLogger.LogError("Error while deleting path: " + serverPath + "\nPointed at by path: " + serverPath, ErrorLogger.Summary.SystemError, e);
				}

				int partNumber = 1;

				try
				{
					while (DeleteFileDirect(contentKey + "-" + partNumber, cancellationToken))
						partNumber++;
				}
				catch (Exception e)
				{
					if (!IsNotFoundException(e))
						_errorLogger.LogError("Error while deleting part #" + partNumber + " for path: " + serverPath + "\nPointed at by path: " + serverPath, ErrorLogger.Summary.SystemError, e);
				}
			}

			do
				newContentKey = _contentKeyGenerator.GenerateContentKey();
			while ((GetFileIDByName(newContentKey, throwIfNotFound: false) != null) || (GetFileIDByName(newContentKey + "-1", throwIfNotFound: false) != null));

			VerboseDiagnosticOutput("[B2] Uploading file to path: {0}", serverPath);
			VerboseDiagnosticOutput("[B2] => New content key: {0}", newContentKey);
			VerboseDiagnosticOutput("[B2] => Uploading {0:#,##0} bytes to content path", contentStream.Length);

			UploadFileImplementation(
				newContentKey,
				contentStream,
				progressCallback,
				cancellationToken);

			VerboseDiagnosticOutput("[B2] => Uploading reference to content key to subject path");

			UploadFileImplementation(
				serverPath,
				new MemoryStream(Encoding.UTF8.GetBytes(newContentKey)),
				progressCallback: null,
				cancellationToken);

			VerboseDiagnosticOutput("[B2] Upload complete");
		}

		public void UploadFilePart(string serverPath, Stream partContentStream, int partNumber, Action<string> newContentKeyCallback, Action<UploadProgress>? progressCallback, CancellationToken cancellationToken)
		{
			VerboseDiagnosticOutput("[B2] Checking for existing file: {0}", serverPath);

			if (DownloadFileStringNoErrorIfNonexistent(serverPath, cancellationToken) is string contentKey)
			{
				VerboseDiagnosticOutput("[B2] => Existing content key: {0}", contentKey);
				VerboseDiagnosticOutput("[B2] => Issuing deletion against non-part content item");

				if (DeleteFileDirect(contentKey, cancellationToken))
					VerboseDiagnosticOutput("[B2]   * File deleted");
			}
			else
			{
				VerboseDiagnosticOutput("[B2] => Assigning content key...");

				do
					contentKey = _contentKeyGenerator.GenerateContentKey();
				while ((GetFileIDByName(contentKey, throwIfNotFound: false) != null) || (GetFileIDByName(contentKey + "-1", throwIfNotFound: false) != null));

				VerboseDiagnosticOutput("[B2] => New content key: {0}", contentKey);
				VerboseDiagnosticOutput("[B2] => Uploading reference to content key to subject path");

				UploadFileImplementation(
					serverPath,
					new MemoryStream(Encoding.UTF8.GetBytes(contentKey)),
					progressCallback: null,
					cancellationToken);
			}

			newContentKeyCallback(contentKey);

			VerboseDiagnosticOutput("[B2] => Uploading {0:#,##0} bytes to part {1} of content path: {2}", partContentStream.Length, partNumber, serverPath);

			string partName = contentKey + "-" + partNumber;

			if (DeleteFileDirect(partName, cancellationToken))
				VerboseDiagnosticOutput("[B2]   * Previously-uploaded part {0} deleted", partNumber);

			UploadFileImplementation(
				partName,
				partContentStream,
				progressCallback,
				cancellationToken);

			VerboseDiagnosticOutput("[B2] Upload complete");
		}

		public Task DownloadFileDirectAsync(string serverPath, Stream contentStream, CancellationToken cancellationToken)
		{
			// BUGS IN BACKBLAZE B2 API: See discussion at top of DownloadFileString.

			if (serverPath.IndexOfAny(B2ProblematicFileNameCharacters) < 0)
			{
				// Fast path: b2_download_file_by_name
				var request = new DownloadFileByNameRequest(FindAndCacheBucketName(), serverPath);

				return _b2Client.DownloadAsync(request, contentStream, default, cancellationToken);
			}
			else
			{
				// Workaround: b2_list_file_versions -> b2_download_file_by_id
				var fileID = GetFileIDByName(serverPath);

				var request = new DownloadFileByIdRequest(fileID);

				return _b2Client.DownloadByIdAsync(request, contentStream, default, cancellationToken);
			}
		}

		public void DownloadFileDirect(string serverPath, Stream contentStream, CancellationToken cancellationToken)
		{
			Wait(DownloadFileDirectAsync(serverPath, contentStream, cancellationToken));
		}

		public async Task<bool> DownloadFilePartDirectAsync(string contentKey, int partNumber, Stream contentStream, CancellationToken cancellationToken)
		{
			var request = new DownloadFileByNameRequest(FindAndCacheBucketName(), contentKey + "-" + partNumber);

			var result = await _b2Client.DownloadAsync(request, contentStream, default, cancellationToken);

			return result.IsSuccessStatusCode;
		}

		public bool DownloadFilePartDirect(string contentKey, int partNumber, Stream contentStream, CancellationToken cancellationToken)
		{
			return Wait(DownloadFilePartDirectAsync(contentKey, partNumber, contentStream, cancellationToken));
		}

		public void DownloadFileByID(string remoteFileID, Stream contentStream, CancellationToken cancellationToken)
		{
			var request = new DownloadFileByIdRequest(remoteFileID);

			Wait(_b2Client.DownloadByIdAsync(request, contentStream, default, cancellationToken));
		}

		public async Task DownloadFileAsync(string serverPath, Stream contentStream, CancellationToken cancellationToken)
		{
			var contentKey = DownloadFileString(serverPath, cancellationToken);

			if (contentKey == null)
				throw new FileNotFoundException("File not found in remote storage: " + serverPath);

			if (GetFileIDByName(contentKey, throwIfNotFound: false) is string fileId)
				await _b2Client.DownloadByIdAsync(fileId, contentStream);
			else
			{
				int partNumber = 1;

				while (true)
				{
					if (GetFileIDByName(contentKey, throwIfNotFound: false) is string partFileId)
						await _b2Client.DownloadByIdAsync(partFileId, contentStream);
					else
					{
						if (partNumber == 1)
							throw new FileNotFoundException("File not found in remote storage: " + serverPath);
						else
							return;
					}
				}
			}
		}

		public void DownloadFile(string serverPath, Stream contentStream, CancellationToken cancellationToken)
		{
			Wait(DownloadFileAsync(serverPath, contentStream, cancellationToken));
		}

		public void MoveFile(string serverPathFrom, string serverPathTo, CancellationToken cancellationToken)
		{
			var contentKey = DownloadFileBytes(serverPathFrom, cancellationToken);

			UploadFileImplementation(
				serverPathTo,
				new MemoryStream(contentKey),
				progressCallback: null,
				cancellationToken);

			string? fileID = null;

			try
			{
				fileID = GetFileIDByName(serverPathFrom);
			}
			catch (Exception e)
			{
				_errorLogger.LogError("Error obtaining file ID for the 'move from' path: " + serverPathFrom, ErrorLogger.Summary.SystemError, exception: e);
			}

			if (fileID != null)
			{
				try
				{
					Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.DeleteAsync(fileID, serverPathFrom)));
				}
				catch (Exception e)
				{
					_errorLogger.LogError("Error while deleting the 'move from' path: " + serverPathFrom + "\nContent key may now be crosslinked: " + contentKey, ErrorLogger.Summary.SystemError, exception: e);
				}
			}
		}

		public void DeleteFile(string serverPath, CancellationToken cancellationToken)
		{
			var contentKey = DownloadFileString(serverPath, cancellationToken);

			if (contentKey != null)
			{
				try
				{
					DeleteFileDirect(serverPath, cancellationToken);
				}
				catch (Exception e)
				{
					if (!IsNotFoundException(e))
						_errorLogger.LogError("Error while deleting path from which a content key was just retrieved: " + serverPath, ErrorLogger.Summary.SystemError, e);
				}

				try
				{
					DeleteFileDirect(contentKey, cancellationToken);
				}
				catch (Exception e)
				{
					if (!IsNotFoundException(e))
						_errorLogger.LogError("Error while deleting path: " + serverPath + "\nPointed at by path: " + serverPath, ErrorLogger.Summary.SystemError, e);
				}

				int partNumber = 1;

				try
				{
					while (DeleteFileDirect(contentKey + "-" + partNumber, cancellationToken))
						partNumber++;
				}
				catch (Exception e)
				{
					if (!IsNotFoundException(e))
						_errorLogger.LogError("Error while deleting part " + partNumber + " for path: " + serverPath + "\nPointed at by path: " + serverPath, ErrorLogger.Summary.SystemError, e);
				}
			}
		}

		public void DeleteFilePart(string serverPath, int partNumber, CancellationToken cancellationToken)
		{
			var contentKey = DownloadFileString(serverPath, cancellationToken);

			if (contentKey == null)
				throw new FileNotFoundException("File not found in remote storage: " + serverPath);

			try
			{
				DeleteFileDirect(contentKey + "-" + partNumber, cancellationToken);
			}
			catch (Exception e)
			{
				if (!IsNotFoundException(e))
					_errorLogger.LogError("Error while deleting part number " + partNumber + " for path: " + serverPath + "\nPointed at by path: " + serverPath, ErrorLogger.Summary.SystemError, e);
			}
		}

		public IEnumerable<RemoteFileInfo> EnumerateFiles(string pathPrefix, bool recursive)
		{
			if ((pathPrefix.Length > 0) && !pathPrefix.EndsWith('/'))
				pathPrefix += '/';

			var request = new ListFileVersionRequest(_parameters.RemoteStorageBucketID);

			request.Prefix = pathPrefix;
			request.MaxFileCount = 1000;

			string lastFileID = "";

			while (true)
			{
				var response = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.ListVersionsAsync(request, TimeSpan.Zero)));

				foreach (var fileItem in response.Response.Files)
				{
					string fileName = fileItem.FileName;

					if (fileName.StartsWith(pathPrefix))
						fileName = fileName.Substring(pathPrefix.Length);
					else if (pathPrefix.Length > 0)
						continue;

					if ((fileName.IndexOf('/') >= 0) && !recursive)
						continue;

					if (fileItem.FileId == lastFileID)
						continue;

					lastFileID = fileItem.FileId;

					var fileInfo = new RemoteFileInfo(
						fileName,
						fileItem.ContentLength,
						fileItem.UploadTimestamp,
						fileItem.FileId);

					yield return fileInfo;
				}

				if (response.Response.NextFileName == null)
					break;

				request.StartFileName = response.Response.NextFileName;
			}
		}

		public IEnumerable<RemoteFileInfo> EnumerateUnfinishedFiles()
		{
			var request = new ListUnfinishedLargeFilesRequest(_parameters.RemoteStorageBucketID);

			while (true)
			{
				var response = Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Files.ListUnfinishedAsync(request, Timeout.InfiniteTimeSpan)));

				response.EnsureSuccessStatusCode();

				if (response.Response.Files.Count == 0)
					break;

				foreach (var fileItem in response.Response.Files)
				{
					var fileInfo = new RemoteFileInfo(
						fileItem.FileName,
						fileItem.ContentLength,
						fileItem.UploadTimestamp,
						fileItem.FileId);

					yield return fileInfo;
				}

				if (response.Response.NextFileId == null)
					break;

				request.StartFileId = response.Response.NextFileId;
			}
		}

		public void CancelUnfinishedFile(string serverPath, CancellationToken cancellationToken)
		{
			if (GetFileIDByName(serverPath, throwIfNotFound: false) is string fileID)
				Wait(AutomaticallyReauthenticateAsync(() => _b2Client.Parts.CancelLargeFileAsync(fileID)));
		}
	}
}
