using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.FileSystem.JSON
{
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public enum DataSetType
	{
		[JsonStringEnumMemberName("FILESYSTEM")]
		FileSystem,
		[JsonStringEnumMemberName("SNAPSHOT")]
		Shapshot,
	}
}
