using System;
using System.IO;

namespace DeltaQ.RTB
{
  public interface IStaging
  {
    IStagedFile StageFile(string path);
    IStagedFile StageFile(Stream path);
  }
}

