using System;
using System.Collections.Generic;

public interface IOpenFileHandles
{
  IEnumerable<OpenFileHandle> Enumerate(string path);
}

