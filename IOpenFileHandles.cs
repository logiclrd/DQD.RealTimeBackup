using System;

public interface IOpenFileHandles
{
  IEnumerable<OpenFileHandle> Enumerate(string path);
}

