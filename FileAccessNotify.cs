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
    int[] pipeFDs = new int[2];

    int result = NativeMethods.pipe(pipeFDs);

    int cancelFD = pipeFDs[0];
    int cancelSignalFD = pipeFDs[1];

    cancellationToken.Register(() => { NativeMethods.write(cancelSignalFD, new byte[1], 1); });

    IntPtr buffer = IntPtr.Zero;

    result = NativeMethods.posix_memalign(ref buffer, 4096, BufferSize);

    if ((result != 0) || (buffer == IntPtr.Zero))
      throw new Exception("Failed to allocate buffer");

    while (!cancellationToken.IsCancellationRequested)
    {
      var pollFDs = new NativeMethods.PollFD[2];

      pollFDs[0].FileDescriptor = _fd;
      pollFDs[0].RequestedEvents = NativeMethods.POLLIN;

      pollFDs[1].FileDescriptor = cancelFD;
      pollFDs[1].RequestedEvents = NativeMethods.POLLIN;

      result = NativeMethods.poll(pollFDs, 1, NativeMethods.INFTIM);

      // For some reason, ReturnedEvents doesn't seem to be being set as expected.
      // So instead, we use the heuristic that the only reason cancelFD would be
      // the reason poll returned is if cancellation is requested. So, if cancellation
      // isn't requested, then we should be good to do a read on _fd.

      if (cancellationToken.IsCancellationRequested)
        break;

      int readSize = NativeMethods.read(_fd, buffer, BufferSize);

      if (readSize < 0)
        throw new Exception("Read error");

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
