using System;

namespace DeltaQ.RTB
{
  public interface IStagedFile : IDisposable
  {
    string Path { get; }
  }
}

