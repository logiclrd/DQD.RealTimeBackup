using System;
using System.Runtime.InteropServices;

namespace DeltaQ.RTB.Tests.Support
{
  public class UnmanagedString : IDisposable
  {
    string _str;
    IntPtr _unmanaged;

    public string String => _str;
    public IntPtr DangerousRawPointer => _unmanaged;

    public UnmanagedString(string str)
    {
      _str = str;
      _unmanaged = Marshal.StringToCoTaskMemAuto(str);
    }

    public void Dispose()
    {
      if (_unmanaged != IntPtr.Zero)
        Marshal.FreeCoTaskMem(_unmanaged);

      _unmanaged = IntPtr.Zero;
    }
  }
}
