using System;

namespace DQD.RealTimeBackup.Web
{
	public class ErrorResult
	{
		public string? ErrorMessage { get; set; }
		public string? StackTrace { get; set; }
		public ErrorResult? InnerException { get; set; }

		public static ErrorResult FromException(Exception ex)
		{
			var result = new ErrorResult();

			result.ErrorMessage = ex.Message;
			result.StackTrace = ex.StackTrace;

			if (ex.InnerException != null)
				result.InnerException = FromException(ex.InnerException);

			return result;
		}
	}
}
