using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.FileSystem.JSON
{
	public class List : CommandOutput
	{
		[JsonPropertyName("datasets")]
		public Dictionary<string, ListDataSet> Datasets { get; set; } = new Dictionary<string, ListDataSet>();
	}
}
