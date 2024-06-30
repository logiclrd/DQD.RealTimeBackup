using System;

using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.Diagnostics
{
	public interface IErrorLogger : IDiagnosticOutput
	{
		ILoggedError LogError(string context, string? summary = null, Exception? exception = null);
		void DisconnectDiagnosticOutput();
	}
}
