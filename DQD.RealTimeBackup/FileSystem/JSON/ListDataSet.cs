using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.FileSystem.JSON
{
	public class ListDataSet
	{
		[JsonPropertyName("name")]
		public string? Name { get; set; }

		[JsonPropertyName("type")]
		public DataSetType Type { get; set; }

		[JsonPropertyName("pool")]
		public string? Pool { get; set; }

		[JsonPropertyName("createtxg")]
		public ulong CreationTransactionGroup { get; set; }

		[JsonPropertyName("dataset")]
		public string? DataSet { get; set; }

		[JsonPropertyName("snapshot_name")]
		public string? SnapshotName { get; set; }

		[JsonPropertyName("properties")]
		public Dictionary<string, DataSetPropertyValue> Properties { get; set; } = new Dictionary<string, DataSetPropertyValue>();

		public string GetStringProperty(string propertyName)
		{
			if (Properties.TryGetValue(propertyName, out var property))
				return property.Value ?? "";
			else
				return "";
		}

		public long GetInt64Property(string propertyName)
		{
			if (Properties.TryGetValue(propertyName, out var property)
			 && long.TryParse(property.Value, out var value))
				return value;
			else
				return -1;
		}
	}
}
