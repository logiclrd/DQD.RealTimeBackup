using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using DQD.RealTimeBackup.Diagnostics;

namespace DQD.RealTimeBackup.Interop
{
	public class FileAccessNotify : IFileAccessNotify, IDisposable
	{
		int _fd;

		IErrorLogger _errorLogger;

		public FileAccessNotify(OperatingParameters parameters, IErrorLogger errorLogger)
		{
			s_fileAccessNotifyDebugLogPath = parameters.FileAccessNotifyDebugLogPath;

			_errorLogger = errorLogger;

			_fd = NativeMethods.fanotify_init(
				FileAccessNotifyFlags.Class.Notification |
				FileAccessNotifyFlags.Report.UniqueFileID |
				FileAccessNotifyFlags.Report.UniqueDirectoryID |
				FileAccessNotifyFlags.Report.IncludeName,
				0);

			if (_fd < 0)
			{
				_errorLogger.LogError("Unable to initialize fanotify, errno = " + Marshal.GetLastWin32Error(), ErrorLogger.Summary.SystemError);
				throw new Exception("Cannot initialize fanotify");
			}
		}

		public void Dispose()
		{
			if (_fd > 0)
			{
				NativeMethods.close(_fd);
				_fd = 0;
			}
		}

		static string? s_fileAccessNotifyDebugLogPath;
		static object s_debugLogSync = new object();

		static bool DebugLogEnabled => (s_fileAccessNotifyDebugLogPath != null);

		static void DebugLog(string line)
		{
			if (DebugLogEnabled)
			{
				lock (s_debugLogSync)
					using (var writer = new StreamWriter(s_fileAccessNotifyDebugLogPath!, append: true))
						writer.WriteLine(line);
			}
		}

		static void DebugLog(object? value = null)
		{
			if (DebugLogEnabled)
				DebugLog(value?.ToString() ?? "");
		}

		static void DebugLog(string format, params object?[] args)
		{
			DebugLog(string.Format(format, args));
		}

		public void MarkPath(string path)
		{
			var mask =
				FileAccessNotifyEventMask.Modified |
				FileAccessNotifyEventMask.ChildDeleted |
				FileAccessNotifyEventMask.ChildMoved;

			int result = NativeMethods.fanotify_mark(
				_fd,
				NativeMethods.FAN_MARK_ADD | NativeMethods.FAN_MARK_FILESYSTEM,
				(long)mask,
				NativeMethods.AT_FDCWD,
				path);

			if (result < 0)
				throw new Exception("[" + Marshal.GetLastWin32Error() + "] Failed to add watch for " + path);
		}

		static internal UnmanagedMemoryStream CreateSubStream(UnmanagedMemoryStream parentStream, int offset = 0, long length = -1)
		{
			unsafe
			{
				if (length < 0)
					length = parentStream.Length - parentStream.Position - offset;

				return new UnmanagedMemoryStream(
					parentStream.PositionPointer + offset,
					length,
					length,
					FileAccess.ReadWrite);
			}
		}

		static internal string? ReadStringAtUnmanagedMemoryStreamCurrentPosition(UnmanagedMemoryStream stream)
		{
			unsafe
			{
				return Marshal.PtrToStringAuto((IntPtr)stream.PositionPointer);
			}
		}

		static internal IEnumerable<FileAccessNotifyEventInfo> ParseEventInfoStructures(UnmanagedMemoryStream eventStream)
		{
			while (eventStream.Position + 4 <= eventStream.Length)
			{
				DebugLog();
				DebugLog("  Event stream: {0} / {1}", eventStream.Position, eventStream.Length);

				// fanotify_event_info_header
				var infoStream = CreateSubStream(eventStream);

				var infoReader = new BinaryReader(infoStream);

				var infoType = (FileAccessNotifyEventInfoType)infoReader.ReadByte();
				var padding = infoReader.ReadByte();
				var infoStructureLength = infoReader.ReadUInt16();

				DebugLog("  Info structure length: {0}", infoStructureLength);
				DebugLog("  => info structure of type: {0}", infoType);

				// Sanity check
				if ((infoStructureLength <= 0) || (infoStructureLength > infoStream.Length))
					break;

				infoStream.SetLength(infoStructureLength);

				eventStream.Position += infoStructureLength;

				var structure = new FileAccessNotifyEventInfo();

				structure.Type = infoType;

				switch (infoType)
				{
					case FileAccessNotifyEventInfoType.FileIdentifier:
					case FileAccessNotifyEventInfoType.ContainerIdentifier:
					case FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName:
					case FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_From:
					case FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_To:
					{
						DebugLog("######## infoType: {0}", infoType);

						structure.FileSystemID = infoReader.ReadInt64();

						// struct file_handle
						// {
						//   uint handle_bytes;
						//   int type;
						//   inline byte[] handle;
						// }
						//
						// handle_bytes is the count of bytes in the handle member alone.
						// So, the overall file_handle is of length handle_bytes + 8, to
						// account for the other fields.

						int handle_bytes = infoReader.ReadInt32();

						infoStream.Position -= 4;

						int file_handle_structure_bytes = handle_bytes + 8;

						structure.FileHandle = new byte[file_handle_structure_bytes];

						infoReader.Read(structure.FileHandle, 0, structure.FileHandle.Length);

						if ((infoType == FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName)
						 || (infoType == FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_From)
						 || (infoType == FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_To))
						{
							structure.FileName = ReadStringAtUnmanagedMemoryStreamCurrentPosition(infoStream);

							DebugLog("  ## {0} Got filename: {1}", infoType, structure.FileName);
						}

						break;
					}
				}

				yield return structure;
			}
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

				DebugLog("got a poll result, cancellation {0}", cancellationToken.IsCancellationRequested);

				// For some reason, ReturnedEvents doesn't seem to be being set as expected.
				// So instead, we use the heuristic that the only reason cancelFD would be
				// the reason poll returned is if cancellation is requested. So, if cancellation
				// isn't requested, then we should be good to do a read on _fd.

				if (cancellationToken.IsCancellationRequested)
					break;

				// The documentation implies that events cannot be split across read calls.
				int readSize = NativeMethods.read(_fd, buffer, BufferSize);

				if (readSize < 0)
				{
					_errorLogger.LogError("Unexpected read error getting data from fanotify, errno = " + Marshal.GetLastWin32Error(), ErrorLogger.Summary.SystemError);
					throw new Exception("Read error");
				}

				DebugLog("Read {0} bytes", readSize);

				unsafe
				{
					var bufferStream = new UnmanagedMemoryStream((byte *)buffer, readSize);

					IntPtr ptr = buffer;
					IntPtr endPtr = ptr + readSize;

					while (bufferStream.Length - bufferStream.Position >= 4) // Continue as long as there is at least an int to be read
					{
						var eventStream = new UnmanagedMemoryStream(
							bufferStream.PositionPointer,
							bufferStream.Length - bufferStream.Position,
							bufferStream.Length - bufferStream.Position,
							FileAccess.ReadWrite);

						var eventReader = new BinaryReader(eventStream);

						int eventLength = eventReader.ReadInt32();

						DebugLog("-----------");
						DebugLog("event length: {0}", eventLength);

						if ((eventLength < NativeMethods.EventHeaderLength) || (ptr + eventLength > endPtr))
							break;

						bufferStream.Position += eventLength;

						eventStream.SetLength(eventLength);

						if (DebugLogEnabled)
						{
							var lineBuilder = new StringBuilder();

							eventStream.Position = 0;
							while (eventStream.Position < eventStream.Length)
								lineBuilder.Append(eventStream.ReadByte().ToString("X2")).Append(' ');
							eventStream.Position = 4; // We have already read eventLength

							DebugLog(lineBuilder);
							DebugLog();
						}

						var metadata = new FileAccessNotifyEventMetadata();

						metadata.Version = eventReader.ReadByte();
						metadata.Reserved = eventReader.ReadByte();
						metadata.MetadataLength = eventReader.ReadInt16();
						metadata.Mask = (FileAccessNotifyEventMask)eventReader.ReadInt64();
						metadata.FileDescriptor = eventReader.ReadInt32();
						metadata.ProcessID = eventReader.ReadInt32();

						DebugLog("  Version: {0}", metadata.Version);
						DebugLog("  Metadata length: {0}", metadata.MetadataLength);
						DebugLog("  Mask: {0}", metadata.Mask);
						DebugLog("  FD: {0}", metadata.FileDescriptor);
						DebugLog("  PID: {0}", metadata.ProcessID);

						var @event = new FileAccessNotifyEvent();

						@event.Metadata = metadata;
						@event.InformationStructures.AddRange(ParseEventInfoStructures(eventStream));

						DebugLog("Raising event");

						eventCallback?.Invoke(@event);

						DebugLog("Event returned");
					}
				}
			}
		}
	}
}

