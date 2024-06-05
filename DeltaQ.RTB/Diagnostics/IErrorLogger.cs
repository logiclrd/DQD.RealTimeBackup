using System;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Diagnostics
{
	public interface IErrorLogger : IDiagnosticOutput
	{
		ILoggedError LogError(string context, string? summary = null, Exception? exception = null);
		void DisconnectDiagnosticOutput();
	}
}
