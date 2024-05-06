using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Microsoft.Win32.SafeHandles;

public class FileAccessNotify : IDisposable
{
  int _fd;

  public FileAccessNotify()
  {
    _fd = NativeMethods.fanotify_init(NativeMethods.FileAccessNotifyClass_Notification | NativeMethods.FileAccessNotifyReport_UniqueFileID, 0);

    if (_fd < 0)
      throw new Exception("Cannot initialize fanotify");
  }

  public void Dispose()
  {
    if (_fd > 0)
    {
      NativeMethods.close(_fd);
      _fd = 0;
    }
  }

  public void MarkPath(string path)
  {
    int result = NativeMethods.fanotify_mark(
      _fd,
      NativeMethods.FAN_MARK_ADD | NativeMethods.FAN_MARK_FILESYSTEM,
      NativeMethods.FAN_ACCESS | NativeMethods.FAN_MODIFY | NativeMethods.FAN_DELETE | NativeMethods.FAN_MOVE,
      NativeMethods.AT_FDCWD,
      path);

    if (result < 0)
      throw new Exception("Failed to add watch for " + path);
  }

  const int BufferSize = 256 * 1024;

  public void MonitorEvents(Action<FileAccessNotifyEvent> eventCallback, CancellationToken cancellationToken)
  {
    var monitorThread = NativeMethods.pthread_self();

    cancellationToken.Register(() => NativeMethods.pthread_kill(monitorThread, NativeMethods.SIGINT));

    IntPtr buffer = IntPtr.Zero;

    int result = NativeMethods.posix_memalign(ref buffer, 4096, BufferSize);

    if ((result != 0) || (buffer == IntPtr.Zero))
      throw new Exception("Failed to allocate buffer");

    while (!cancellationToken.IsCancellationRequested)
    {
      int readSize = NativeMethods.read(_fd, buffer, BufferSize);

      if (readSize < 0)
      {
        if (Marshal.GetLastWin32Error() == NativeMethods.EINTR)
          return;

        throw new Exception("Read error");
      }

      unsafe
      {
        IntPtr ptr = buffer;
        IntPtr endPtr = ptr + readSize;

        while (true)
        {
          int eventLength = Marshal.ReadInt32(ptr);

          if ((eventLength < NativeMethods.EventHeaderLength) || (ptr + eventLength > endPtr))
            break;

          var additionalDataPtr = ptr + NativeMethods.EventHeaderLength;

          ptr += eventLength;

          var eventStream = new UnmanagedMemoryStream((byte *)ptr, eventLength);

          var eventReader = new BinaryReader(eventStream);

          var metadata = new FileAccessNotifyEventMetadata();

          metadata.Version = eventReader.ReadByte();
          metadata.Reserved = eventReader.ReadByte();
          metadata.MetadataLength = eventReader.ReadInt16();
          metadata._Alignment = eventReader.ReadInt32();
          metadata.Mask = eventReader.ReadInt64();
          metadata.FileDescriptor = eventReader.ReadInt32();
          metadata.ProcessID = eventReader.ReadInt32();

          var @event = new FileAccessNotifyEvent();

          @event.Metadata = metadata;
          @event.AdditionalData = additionalDataPtr;
          @event.AdditionalDataLength = eventLength - NativeMethods.EventHeaderLength;

          eventCallback?.Invoke(@event);
        }
      }
    }
  }
}
