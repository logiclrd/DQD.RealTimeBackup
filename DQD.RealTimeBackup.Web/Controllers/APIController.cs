using System;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using DQD.RealTimeBackup.StateCache;
using System.Collections.Generic;

namespace DQD.RealTimeBackup.Web
{
	[Route("api")]
	public class APIController : Controller
	{
		ISessionManager _sessionManager;

		Func<IRemoteFileStateCache> _remoteFileStateCacheFactory;

		public APIController(ISessionManager sessionManager, Func<IRemoteFileStateCache> remoteFileStateCacheBuilder)
		{
			_sessionManager = sessionManager;
			_remoteFileStateCacheFactory = remoteFileStateCacheBuilder;
		}

		[HttpPost("StartSession")]
		public JsonResult StartSession()
		{
			try
			{
				var session = _sessionManager.StartSession();

				session.BeginLoadFileState(_remoteFileStateCacheFactory);

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
		public JsonResult GetSessionStatus(string sessionID)
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

		[HttpPost("GetChildItems")]
		public JsonResult GetChildItems([FromBody] GetChildItemsRequest request)
		{
			try
			{
				var session = _sessionManager.GetSession(request.SessionID ?? throw new NullReferenceException("SessionID"));

				if (session == null)
					throw new KeyNotFoundException();

				if (request.ParentPath == null)
					throw new Exception("Invalid request");

				var result = new GetChildItemsResponse();

				foreach (var childPath in session.GetDirectoriesInDirectory(request.ParentPath, recursive: false))
					result.Directories.Add(childPath);

				foreach (var child in session.GetFilesInDirectory(request.ParentPath, recursive: false))
				{
					var fileInfo = new FileInformation();

					fileInfo.Path = child.Path;
					fileInfo.FileSize = child.FileSize;
					fileInfo.LastModifiedUTC = child.LastModifiedUTC;

					result.Files.Add(fileInfo);
				}

				return Json(result);
			}
			catch (Exception e)
			{
				var result = Json(ErrorResult.FromException(e));

				result.StatusCode = StatusCodes.Status500InternalServerError;

				return result;
			}
		}
	}
}
