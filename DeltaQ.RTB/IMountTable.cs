using System;
using System.Collections.Generic;

public interface IMountTable
{
  MountHandle OpenMountForFileSystem(string mountPointPath);
  IEnumerable<Mount> EnumerateMounts();
}

