using System;

namespace DQD.RealTimeBackup.Utility
{
	public interface IDiagnosticOutput
	{
		event EventHandler<DiagnosticMessage> DiagnosticOutput;
	}
}
