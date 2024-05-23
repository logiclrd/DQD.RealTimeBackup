using System;

namespace DeltaQ.RTB.Utility
{
	public class DiagnosticOutputHook : IDisposable
	{
		IDiagnosticOutput _subject;
		Action<string> _handler;

		public DiagnosticOutputHook(IDiagnosticOutput subject, Action<string> handler)
		{
			_subject = subject;
			_handler = handler;

			_subject.DiagnosticOutput += subject_DiagnosticOutput;
		}

		public void Dispose()
		{
			_subject.DiagnosticOutput -= subject_DiagnosticOutput;
		}

		void subject_DiagnosticOutput(object? sender, string message)
		{
			WriteLine(message);
		}

		public void WriteLine(string format, object[] args)
		{
			WriteLine(string.Format(format, args));
		}

		public void WriteLine(string message)
		{
			_handler(message);
		}
	}
}
