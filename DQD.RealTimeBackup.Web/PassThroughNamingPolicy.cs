using System.Text.Json;

namespace DQD.RealTimeBackup.Web
{
	public class PassThroughNamingPolicy : JsonNamingPolicy
	{
		public override string ConvertName(string name) => name;
	}
}
