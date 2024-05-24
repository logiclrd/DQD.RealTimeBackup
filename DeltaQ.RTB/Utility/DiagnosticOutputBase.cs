using System;

namespace DeltaQ.RTB.Utility
{
	public abstract class DiagnosticOutputBase : IDiagnosticOutput
	{
		public event EventHandler<DiagnosticMessage>? DiagnosticOutput;

		protected virtual void OnDiagnosticOutput(Importance importance, string line)
		{
			DiagnosticOutput?.Invoke(
				this,
				importance switch
				{
					Importance.Normal => DiagnosticMessage.NonQuiet(line),
					Importance.VerboseOnly => DiagnosticMessage.Verbose(line),
					Importance.HideWhenQuiet => new DiagnosticMessage(line),

					_ => new DiagnosticMessage(line),
				});
		}

		protected virtual void OnDiagnosticOutput(string line)
		{
			OnDiagnosticOutput(Importance.Normal, line);
		}

		protected virtual void OnDiagnosticOutput(string format, params object?[] args)
		{
			if (DiagnosticOutput != null)
				OnDiagnosticOutput(string.Format(format, args));
		}

		protected virtual void VerboseDiagnosticOutput(string line)
		{
			OnDiagnosticOutput(Importance.VerboseOnly, line);
		}

		protected virtual void VerboseDiagnosticOutput(string format, params object?[] args)
		{
			if (DiagnosticOutput != null)
				OnDiagnosticOutput(Importance.VerboseOnly, string.Format(format, args));
		}

		protected virtual void NonQuietDiagnosticOutput(string line)
		{
			OnDiagnosticOutput(Importance.HideWhenQuiet, line);
		}

		protected virtual void NonQuietDiagnosticOutput(string format, params object?[] args)
		{
			if (DiagnosticOutput != null)
				OnDiagnosticOutput(Importance.HideWhenQuiet, string.Format(format, args));
		}
	}
}
