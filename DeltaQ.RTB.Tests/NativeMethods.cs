using System;
using System.Runtime.InteropServices;

namespace DeltaQ.RTB.Tests
{
  class NativeMethods
  {
    [DllImport("c", SetLastError = true)]
    public static extern int geteuid();
  }
}

