using System.IO;

public interface IChecksum
{
  string ComputeChecksum(Stream stream);
  string ComputeChecksum(string path);
}
