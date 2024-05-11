using System;
using System.IO;

namespace DeltaQ.RTB.Tests
{
  class TemporaryFile : IDisposable
  {
    string _path;
    bool _disposed;

    static Random s_rnd = new Random();

    public string Path => _path;

    public TemporaryFile()
      : this("/tmp/" + DateTime.UtcNow.Ticks + "-" + s_rnd.NextInt64())
    {
    }

    public TemporaryFile(string path)
    {
      _path = path;
    }

    public void Dispose()
    {
      if (!_disposed)
      {
        File.Delete(_path);
        _disposed = true;
      }
    }
  }
}
