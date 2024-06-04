using System;
using System.Collections.Generic;
using DeltaQ.RTB.Bridge.Serialization;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class ErrorInfo
	{
		[FieldOrder(0)]
		public string? Message;
		[FieldOrder(1)]
		public string? Source;
		[FieldOrder(2)]
		public string? StackTrace;
		[FieldOrder(3)]
		public ErrorInfo? InnerError;
		[FieldOrder(4)]
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
