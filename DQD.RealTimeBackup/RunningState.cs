using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using DQD.RealTimeBackup.ActivityMonitor;
using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.FileSystem;
using DQD.RealTimeBackup.Interop;
using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup;

public class RunningState
{
	public static readonly RunningState Instance = new RunningState();

	public readonly RunningState_FileSystemMonitor FileSystemMonitor = new RunningState_FileSystemMonitor();
	public readonly RunningState_RemoteFileStateCacheActionThread RemoteFileStateCacheActionThread = new RunningState_RemoteFileStateCacheActionThread();
	public readonly RunningState_PollOpenFilesThread PollOpenFilesThread = new RunningState_PollOpenFilesThread();
	public readonly RunningState_LongPollingThread LongPollingThread = new RunningState_LongPollingThread();
	public readonly RunningState_ProcessBackupQueueThread ProcessBackupQueueThread = new RunningState_ProcessBackupQueueThread();
	public readonly RunningState_UploadThreads UploadThreads = new RunningState_UploadThreads();

	public override string ToString()
	{
		var serializerOptions = new JsonSerializerOptions();

		serializerOptions.IncludeFields = true;
		serializerOptions.WriteIndented = true;
		serializerOptions.Converters.Add(new JsonStringEnumConverter());
		serializerOptions.Converters.Add(new JsonExceptionConverter());

		return JsonSerializer.Serialize(this, serializerOptions);
	}

	public class RunningState_FileSystemMonitor
	{
		public FileSystemMonitorState State;
		public FileAccessNotifyEvent? Event;
	}

	public class RunningState_RemoteFileStateCacheActionThread
	{
		public RemoteFileStateCacheActionThreadState State;
		public CacheAction? Action;
		public bool PerformingUpload;
		public bool PerformingDeletion;
		public Exception? Exception;
	}

	public class RunningState_PollOpenFilesThread
	{
		public PollOpenFilesThreadState State;
		public SnapshotReference? CurrentFile;
		public Exception? Exception;
	}

	public class RunningState_LongPollingThread
	{
		public LongPollingThreadState State;
		public SnapshotReference? CurrentFile;
		public bool ComparingFiles;
		public Exception? Exception;
	}

	public class RunningState_ProcessBackupQueueThread
	{
		public ProcessBackupQueueThreadState State;
		public BackupAction? Action;
		public string? Path;
		public string? FromPath;
		public int LastPartNumber;
		public Exception? Exception;
	}

	public class RunningState_UploadThreads : IEnumerable<RunningState_UploadThread>
	{
		object _sync = new object();
		List<RunningState_UploadThread> _threads = new List<RunningState_UploadThread>();

		public RunningState_UploadThread Register(int threadIndex)
		{
			var thread = new RunningState_UploadThread();

			thread.ThreadIndex = threadIndex;

			lock (_sync)
				_threads.Add(thread);

			thread.OnDispose =
				() =>
				{
					lock (_sync)
						_threads.Remove(thread);
				};

			return thread;
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

		public IEnumerator<RunningState_UploadThread> GetEnumerator()
		{
			lock (_sync)
				return _threads.ToList().GetEnumerator();
		}
	}

	public class RunningState_UploadThread : IDisposable
	{
		internal Action? OnDispose;

		public void Dispose()
		{
			OnDispose?.Invoke();
		}

		public int ThreadIndex;
		public UploadThreadState State;
		public Exception? Exception;
		public FileReference? File;
		public int PartNumber;
		public bool PerformingUpload;
		public bool PerformingDeletion;
	}
}