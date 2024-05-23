using System;

namespace DeltaQ.RTB.Utility
{
	public abstract class DiagnosticOutputBase : IDiagnosticOutput
	{
		public event EventHandler<string>? DiagnosticOutput;

		protected virtual void OnDiagnosticOutput(string line)
		{
			DiagnosticOutput?.Invoke(this, line);
		}

		protected virtual void OnDiagnosticOutput(string format, params object[] args)
		{
			if (DiagnosticOutput != null)
				OnDiagnosticOutput(string.Format(format, args));
		}
	}
}
