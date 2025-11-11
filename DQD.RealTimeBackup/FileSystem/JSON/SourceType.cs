using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.FileSystem.JSON
{
	[JsonConverter(typeof(JsonStringEnumConverter<SourceType>))]
	public enum SourceType
	{
		Unknown,

		[JsonStringEnumMemberName("NONE")]
		None,

		[JsonStringEnumMemberName("DEFAULT")]
		Default,

		[JsonStringEnumMemberName("LOCAL")]
		Local,
	}
}
