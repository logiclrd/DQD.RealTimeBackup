using System;
using System.Collections.Generic;

using DeltaQ.RTB.Bridge.Serialization;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class ErrorInfo
	{
		[FieldOrder(0)]
		public string? ExceptionType;
		[FieldOrder(1)]
		public string? Message;
		[FieldOrder(2)]
		public string? Source;
		[FieldOrder(3)]
		public string? StackTrace;
		[FieldOrder(4)]
		public ErrorInfo? InnerError;
		[FieldOrder(5)]
		public List<ErrorInfo?>? InnerErrors;

		public ErrorInfo()
		{
		}

		public ErrorInfo(Exception ex)
		{
			ExceptionType = ex.GetType().FullName;
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
