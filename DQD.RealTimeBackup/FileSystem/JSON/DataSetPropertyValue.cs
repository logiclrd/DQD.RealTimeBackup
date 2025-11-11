using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.FileSystem.JSON
{
	public class DataSetPropertyValue
	{
		[JsonPropertyName("value")]
		public string? Value { get; set; }

		[JsonPropertyName("source")]
		public DataSetPropertySource? Source { get; set; }
	}
}
