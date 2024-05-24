using System;

namespace DeltaQ.RTB.Utility
{
	public interface IDiagnosticOutput
	{
		event EventHandler<DiagnosticMessage> DiagnosticOutput;
	}
}
