using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.FileSystem.JSON
{
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public enum DataSetType
	{
		[JsonStringEnumMemberName("UNKNOWN")]
		Unknown,

		[JsonStringEnumMemberName("FILESYSTEM")]
		FileSystem,
		[JsonStringEnumMemberName("VOLUME")]
		Volume,
		[JsonStringEnumMemberName("SNAPSHOT")]
		Shapshot,
		[JsonStringEnumMemberName("POOL")]
		Pool,
		[JsonStringEnumMemberName("BOOKMARK")]
		Bookmark,
	}
}
