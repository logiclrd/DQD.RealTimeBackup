using System.Security.Cryptography;

public class FileState
{
  public string Path;
  public long FileSize;
  public DateTime LastModifiedUTC;
  public string Checksum;

  private FileState()
  {
    // Dummy constructor.
    Path = Checksum = "";
  }

  public static FileState FromFile(string path)
  {
    var ret = new FileState();

    ret.Path = path;
    ret.LastModifiedUTC = File.GetLastWriteTimeUtc(path);

    using (var stream = File.OpenRead(path))
    {
      ret.FileSize = stream.Length;
      ret.Checksum = ComputeChecksum(stream);
    }

    return ret;
  }

  public bool IsMatch()
  {
    if (!File.Exists(Path))
      return false;

    using (var stream = File.OpenRead(Path))
    {
      if (stream.Length != FileSize)
        return false;

      if (ComputeChecksum(stream) != Checksum)
        return false;

      return true;
    }
  }

  public static string ComputeChecksum(Stream stream)
  {
    using (var md5 = MD5.Create())
    {
      var checksumBytes = md5.ComputeHash(stream);

      char[] checksumChars = new char[checksumBytes.Length * 2];

      for (int i=0; i < checksumBytes.Length; i++)
      {
        byte b = checksumBytes[i];

        checksumChars[i + i] = "0123456789abcdef"[b >> 4];
        checksumChars[i+i+1] = "0123456789abcdef"[b & 15];
      }

      return new string(checksumChars);
    }
  }

  public static FileState Parse(string serialized)
  {
    string[] parts = serialized.Split(' ', 3);

    var ret = new FileState();

    ret.Path = parts[3];
    ret.LastModifiedUTC = new DateTime(ticks: long.Parse(parts[2]), DateTimeKind.Utc);
    ret.Checksum = parts[0];
    ret.FileSize = long.Parse(parts[1]);

    return ret;
  }

  public override string ToString()
  {
    if (Path.IndexOf('\n') >= 0)
      throw new Exception("Sanity failure: Path contains a newline character.");

    return $"{Checksum} {FileSize} {LastModifiedUTC.Ticks} {Path}";
  }
}

