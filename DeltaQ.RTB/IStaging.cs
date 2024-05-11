using System;
using System.IO;

public interface IStaging
{
  IStagedFile StageFile(string path);
  IStagedFile StageFile(Stream path);
}

