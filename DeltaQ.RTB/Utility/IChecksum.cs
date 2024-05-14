using System.IO;

namespace DeltaQ.RTB.Utility
{
	public interface IChecksum
	{
		string ComputeChecksum(Stream stream);
		string ComputeChecksum(string path);
	}
}
