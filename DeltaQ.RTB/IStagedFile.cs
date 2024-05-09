using System;

public interface IStagedFile : IDisposable
{
  string Path { get; }
}

