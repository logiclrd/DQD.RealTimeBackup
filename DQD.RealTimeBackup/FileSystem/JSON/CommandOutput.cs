using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.FileSystem.JSON
{
	public class CommandOutput
	{
		[JsonPropertyName("output_version")]
		public CommandOutputVersion? OutputVersion { get; set; }
	}
}
