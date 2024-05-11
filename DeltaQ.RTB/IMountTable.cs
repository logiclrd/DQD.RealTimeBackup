using System;
using System.Collections.Generic;

namespace DeltaQ.RTB
{
  public interface IMountTable
  {
    MountHandle OpenMountForFileSystem(string mountPointPath);
    IEnumerable<Mount> EnumerateMounts();
  }
}

