using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.FileSystem.JSON
{
	public class CommandOutputVersion
	{
		[JsonPropertyName("command")]
		public string? Command { get; set; }

		[JsonPropertyName("vers_major")]
		public int MajorVersion { get; set; }

		[JsonPropertyName("vers_minor")]
		public int MinorVersion { get; set; }
	}
}
