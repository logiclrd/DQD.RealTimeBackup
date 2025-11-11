using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.FileSystem.JSON
{
	public class DataSetPropertySource
	{
		[JsonPropertyName("type")]
		public SourceType Type { get; set; } = SourceType.Unknown;

		[JsonPropertyName("data")]
		public string? Data { get; set; }
	}
}
