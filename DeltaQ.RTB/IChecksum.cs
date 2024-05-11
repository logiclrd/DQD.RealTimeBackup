using System.IO;

namespace DeltaQ.RTB
{
  public interface IChecksum
  {
    string ComputeChecksum(Stream stream);
    string ComputeChecksum(string path);
  }
}
