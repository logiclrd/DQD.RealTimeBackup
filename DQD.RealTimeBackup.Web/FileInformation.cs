using System;

namespace DQD.RealTimeBackup.Web
{
	public class FileInformation
	{
		public string? Path { get; set; }
		public long FileSize { get; set; }
		public DateTime LastModifiedUTC { get; set; }
	}
}
