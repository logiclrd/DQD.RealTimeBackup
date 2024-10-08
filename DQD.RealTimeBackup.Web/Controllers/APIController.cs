using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.BZip2;

using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.Agent;

namespace DQD.RealTimeBackup.Web
{
	[Route("api")]
	public class APIController : Controller
	{
		OperatingParameters _parameters;
		ISessionManager _sessionManager;

		static string s_nextSalt;

		IRemoteStorage _remoteStorage;
		Func<IRemoteFileStateCache> _remoteFileStateCacheFactory;

		static APIController()
		{
			s_nextSalt = "";

			RotateSalt();
		}

		public APIController(OperatingParameters parameters, ISessionManager sessionManager, IRemoteStorage remoteStorage, Func<IRemoteFileStateCache> remoteFileStateCacheBuilder)
		{
			_parameters = parameters;
			_sessionManager = sessionManager;
			_remoteStorage = remoteStorage;
			_remoteFileStateCacheFactory = remoteFileStateCacheBuilder;
		}

		public static void RotateSalt()
		{
			s_nextSalt = Guid.NewGuid().ToString();
		}

		string GetCorrectPasswordHash()
		{
			string unsaltedPasswordHash = _parameters.WebAccessPasswordHash ?? "blorg";

			byte[] buffer = Encoding.UTF8.GetBytes("salty" + s_nextSalt + unsaltedPasswordHash + "salty");

			byte[] hash = SHA512.Create().ComputeHash(buffer);

			char[] hashChars = new char[hash.Length * 2];

			char ToHex(int value) => "0123456789abcdef"[value];

			for (int i=0; i < hash.Length; i++)
			{
				hashChars[i + i] = ToHex(hash[i] >> 4);
				hashChars[i + i + 1] = ToHex(hash[i] & 15);
			}

			return new string(hashChars);
		}

		[HttpGet("GetNextSaltForPasswordHash")]
		public string GetNextSaltForPasswordHash()
		{
			return s_nextSalt;
		}

		[HttpPost("StartSession")]
		public ActionResult StartSession([FromBody] StartSessionRequest request)
		{
			try
			{
				var correctPasswordHash = GetCorrectPasswordHash();

				if (request.PasswordHash != correctPasswordHash)
				{
					var result = Json(
						new
						{
							ErrorMessage = "Login failed: Incorrect password"
						});

					result.StatusCode = StatusCodes.Status401Unauthorized;

					return result;
				}

				var session = _sessionManager.StartSession();

				session.BeginLoadFileState(_remoteFileStateCacheFactory);

				RotateSalt();

				return Json(
					new StartSessionResult()
					{
						SessionID = session.SessionID
					});
			}
			catch (Exception e)
			{
				var result = Json(ErrorResult.FromException(e));

				result.StatusCode = StatusCodes.Status500InternalServerError;

				return result;
			}
		}

		[HttpGet("GetSessionStatus")]
		public JsonResult GetSessionStatus([FromQuery] string sessionID)
		{
			try
			{
				var session = _sessionManager.GetSession(sessionID);

				JsonResult result;

				if (session == null)
				{
					result = Json(new { Status = "Not Found" });
					result.StatusCode = StatusCodes.Status404NotFound;
				}
				else
					result = Json(GetSessionStatusResult.FromSession(session));

				return result;
			}
			catch (Exception e)
			{
				var result = Json(ErrorResult.FromException(e));

				result.StatusCode = StatusCodes.Status500InternalServerError;

				return result;
			}
		}

		[HttpPost("TerminateSession")]
		public ActionResult TerminateSession([FromBody] TerminateSessionRequest request)
		{
			try
			{
				if (request.SessionID == null)
				{
					var result = Json(new { ErrorMessage = "Invalid request: no SessionID supplied" });

					result.StatusCode = StatusCodes.Status400BadRequest;

					return result;
				}

				_sessionManager.EndSession(request.SessionID);

				return Json(
					new TerminateSessionResult()
					{
						SessionID = request.SessionID,
						Success = true,
					});
			}
			catch (Exception e)
			{
				var result = Json(ErrorResult.FromException(e));

				result.StatusCode = StatusCodes.Status500InternalServerError;

				return result;
			}
		}

		[HttpPost("GetChildItems")]
		public ActionResult GetChildItems([FromBody] GetChildItemsRequest request)
		{
			try
			{
				var session = _sessionManager.GetSession(request.SessionID ?? throw new NullReferenceException("SessionID"));

				if (session == null)
				{
					var result = Json(new { SessionExpired = true });

					result.StatusCode = StatusCodes.Status401Unauthorized;

					return result;
				}
				else
				{
					if (request.ParentPath == null)
						throw new Exception("Invalid request");

					var result = new GetChildItemsResult();

					foreach (var childPath in session.GetDirectoriesInDirectory(request.ParentPath, recursive: false))
						result.Directories.Add(childPath);

					foreach (var child in session.GetFilesInDirectory(request.ParentPath, recursive: false))
						result.Files.Add(child);

					return Json(result);
				}
			}
			catch (Exception e)
			{
				var result = Json(ErrorResult.FromException(e));

				result.StatusCode = StatusCodes.Status500InternalServerError;

				return result;
			}
		}

		[HttpGet("DownloadSingleFile")]
		public IActionResult DownloadSingleFile([FromQuery] string sessionID, [FromQuery] int fileIndex)
		{
			try
			{
				var session = _sessionManager.GetSession(sessionID ?? throw new NullReferenceException("SessionID"));

				// Session expired?
				if (session == null)
					return Redirect("index.html");

				var fileState = session.GetFileByIndex(fileIndex);

				return new SingleFileDownloadResult(_parameters, _remoteStorage, fileState);
			}
			catch (Exception e)
			{
				var result = Json(ErrorResult.FromException(e));

				result.StatusCode = StatusCodes.Status500InternalServerError;

				return result;
			}
		}

		[HttpPost("DownloadMultipleFiles")]
		public IActionResult DownloadMultipleFiles([FromForm] string sessionID, [FromForm(Name = "FileIndices")] string fileIndicesString)
		{
			try
			{
				var session = _sessionManager.GetSession(sessionID ?? throw new NullReferenceException("SessionID"));

				// Session expired?
				if (session == null)
					return Redirect("index.html");

				var fileIndices = fileIndicesString.Split(',').Select(str => int.Parse(str));

				var fileStates = fileIndices.Select(fileIndex => session.GetFileByIndex(fileIndex)).ToList();

				return new MultipleFileDownloadResult(_parameters, _remoteStorage, fileStates);
			}
			catch (Exception e)
			{
				var result = Json(ErrorResult.FromException(e));

				result.StatusCode = StatusCodes.Status500InternalServerError;

				return result;
			}
		}

		class SingleFileDownloadResult : IActionResult
		{
			OperatingParameters _parameters;
			IRemoteStorage _remoteStorage;
			FileState _file;

			public SingleFileDownloadResult(OperatingParameters parameters, IRemoteStorage remoteStorage, FileState file)
			{
				_parameters = parameters;
				_remoteStorage = remoteStorage;
				_file = file;
			}

			public Task ExecuteResultAsync(ActionContext context)
			{
				var response = context.HttpContext.Response;

				string fileName = Path.GetFileName(_file.Path);

				byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);

				string encodedFileName = System.Web.HttpUtility.UrlEncode(fileNameBytes);

				response.Headers.ContentType = "application/octet-stream";
				response.Headers.ContentDisposition = "attachment; filename*=UTF-8''" + encodedFileName;

				var serverPath = BackupAgent.PlaceInContentPath(_file.Path);

				if (_file.IsInParts)
					return DownloadFileParts(response.Body, context.HttpContext.RequestAborted);
				else
					return _remoteStorage.DownloadFileAsync(
						serverPath,
						response.Body,
						context.HttpContext.RequestAborted);
			}

			async Task DownloadFileParts(Stream bodyStream, CancellationToken cancellationToken)
			{
				int partCount = (int)((_file.FileSize + _parameters.FilePartSize - 1) / _parameters.FilePartSize);

				for (int partNumber = 1; partNumber <= partCount; partNumber++)
				{
					bool succeeded = await _remoteStorage.DownloadFilePartDirectAsync(
						_file.ContentKey,
						partNumber,
						bodyStream,
						cancellationToken);

					if (!succeeded)
						break;
				}
			}
		}

		class MultipleFileDownloadResult : IActionResult
		{
			OperatingParameters _parameters;
			IRemoteStorage _remoteStorage;
			IEnumerable<FileState> _files;

			public MultipleFileDownloadResult(OperatingParameters parameters, IRemoteStorage remoteStorage, IEnumerable<FileState> files)
			{
				_parameters = parameters;
				_remoteStorage = remoteStorage;
				_files = files;
			}

			public async Task ExecuteResultAsync(ActionContext context)
			{
				var response = context.HttpContext.Response;

				string fileName = "restore-" + _files.Count() + "_files-" + DateTime.Now.ToString("yyyy_MM_dd-HH_mm") + ".tar.bz2";

				byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);

				string encodedFileName = System.Web.HttpUtility.UrlEncode(fileNameBytes);

				response.Headers.ContentType = "application/octet-stream";
				response.Headers.ContentDisposition = "attachment; filename*=UTF-8''" + encodedFileName;

				using (var compressor = await BZip2AsyncOutputStream.CreateAsync(response.Body, context.HttpContext.RequestAborted))
				using (var tar = new TarOutputStream(compressor, Encoding.UTF8))
				{
					var incompleteEntriesMessage = new StringWriter();

					foreach (var file in _files)
					{
						var tarEntry = TarEntry.CreateTarEntry(file.Path.TrimStart('/'));

						tarEntry.Size = file.FileSize;

						await tar.PutNextEntryAsync(tarEntry, response.HttpContext.RequestAborted);

						var serverPath = BackupAgent.PlaceInContentPath(file.Path);

						if (!file.IsInParts)
						{
							await _remoteStorage.DownloadFileAsync(
								serverPath,
								tar,
								context.HttpContext.RequestAborted);
						}
						else
						{
							int partCount = (int)((file.FileSize + _parameters.FilePartSize - 1) / _parameters.FilePartSize);

							for (int partNumber = 1; partNumber <= partCount; partNumber++)
							{
								bool succeeded = await _remoteStorage.DownloadFilePartDirectAsync(
									file.ContentKey,
									partNumber,
									tar,
									context.HttpContext.RequestAborted);

								if (!succeeded)
								{
									incompleteEntriesMessage.WriteLine("File is incomplete: {0}", file.Path);
									incompleteEntriesMessage.WriteLine();
									if (partNumber > 1)
										incompleteEntriesMessage.WriteLine("Read parts 1 through {0} successfully", partNumber - 1);
									incompleteEntriesMessage.WriteLine("Part number {0} could not be retrieved from remote storage", partNumber);
									incompleteEntriesMessage.WriteLine();
								}
							}
						}

						await tar.CloseEntryAsync(context.HttpContext.RequestAborted);
					}

					byte[] incompleteEntriesMessageBytes = Encoding.UTF8.GetBytes(incompleteEntriesMessage.ToString());

					if (incompleteEntriesMessageBytes.Length > 0)
					{
						var tarEntry = TarEntry.CreateTarEntry("INCOMPLETE");

						tarEntry.Size = incompleteEntriesMessageBytes.Length;

						await tar.PutNextEntryAsync(tarEntry, context.HttpContext.RequestAborted);
						await tar.WriteAsync(incompleteEntriesMessageBytes, 0, incompleteEntriesMessageBytes.Length, context.HttpContext.RequestAborted);
						await tar.CloseEntryAsync(context.HttpContext.RequestAborted);
					}
				}
			}
		}
	}
}
