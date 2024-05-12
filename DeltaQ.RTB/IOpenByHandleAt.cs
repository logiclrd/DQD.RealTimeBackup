using System;

namespace DeltaQ.RTB
{
  public interface IOpenByHandleAt
  {
    IOpenHandle? Open(int mountFileDescriptor, IntPtr fileHandle);
  }
}

