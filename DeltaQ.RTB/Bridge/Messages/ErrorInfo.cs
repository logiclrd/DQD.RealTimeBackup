using System;
using System.Collections.Generic;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class ErrorInfo
	{
		public string? Message;
		public string? Source;
		public string? StackTrace;
		public ErrorInfo? InnerError;
		public List<ErrorInfo?>? InnerErrors;

		public ErrorInfo()
		{
		}

		public ErrorInfo(Exception ex)
		{
			Message = ex.Message;
			Source = ex.Source;
			StackTrace = ex.StackTrace;

			if (ex.InnerException != null)
				InnerError = new ErrorInfo(ex.InnerException);

			if (ex is AggregateException aggregateException)
			{
				InnerErrors = new List<ErrorInfo?>();

				foreach (var exception in aggregateException.InnerExceptions)
					InnerErrors.Add(new ErrorInfo(exception));
			}
		}
	}
}
