namespace DeltaQ.RTB.Utility
{
	public class DiagnosticMessage
	{
		public string Message;
		public bool IsVerbose;
		public bool IsUnimportant;

		public DiagnosticMessage(string message)
		{
			Message = message;
			IsUnimportant = true;
		}

		public static DiagnosticMessage Verbose(string message)
			=> new DiagnosticMessage(message) { IsVerbose = true };

		public static DiagnosticMessage NonQuiet(string message)
			=> new DiagnosticMessage(message) { IsUnimportant = false };

	}
}
