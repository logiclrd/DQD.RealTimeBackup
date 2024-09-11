using System.Collections.Generic;

namespace DQD.RealTimeBackup.Web
{
	public class GetChildItemsResult
	{
		public List<string> Directories { get; set; } = new List<string>();
		public List<FileInformation> Files { get; set; } = new List<FileInformation>();
	}
}
