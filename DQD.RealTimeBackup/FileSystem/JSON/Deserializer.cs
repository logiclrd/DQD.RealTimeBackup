using System.Text.Json;
using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.FileSystem.JSON
{
	public static class Deserializer
	{
		public static JsonSerializerOptions CreateOptions()
		{
			var options =
				new JsonSerializerOptions()
				{
					NumberHandling = JsonNumberHandling.AllowReadingFromString,
				};

			return options;
		}
	}
}
