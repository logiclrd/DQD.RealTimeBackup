using System.IO;

namespace DQD.RealTimeBackup.Utility
{
	public interface IChecksum
	{
		string ComputeChecksum(Stream stream);
		string ComputeChecksum(string path);
	}
}
