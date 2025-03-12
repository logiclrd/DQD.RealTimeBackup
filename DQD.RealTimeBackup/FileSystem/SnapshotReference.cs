using System;
using System.IO;

using DQD.RealTimeBackup.Diagnostics;

namespace DQD.RealTimeBackup.FileSystem
{
	public class SnapshotReference : IDisposable
	{
		SnapshotReferenceTracker? _tracker;
		string _path;
		string? _snapshottedPath;
		long _snapshottedFileSize;

		IErrorLogger _errorLogger;

		public SnapshotReference(SnapshotReferenceTracker tracker, string path, IErrorLogger errorLogger)
		{
			_tracker = tracker;
			_path = path;

			_errorLogger = errorLogger;
		}

		public void Dispose()
		{
			_tracker?.Release(this);
			_tracker = null;
		}

		string ResolveSnapshottedPath()
		{
			if (_snapshottedPath == null)
			{
				if (_tracker!.Snapshot.MountPoint == "/")
					_snapshottedPath = System.IO.Path.Combine(_tracker?.Snapshot.BuildPath() ?? "", _path.TrimStart('/'));
				else
				{
					string subPath = _path;

					if (subPath.StartsWith(_tracker.Snapshot.MountPoint)
					&& (subPath[_tracker.Snapshot.MountPoint.Length] == '/'))
						subPath = subPath.Substring(_tracker.Snapshot.MountPoint.Length + 1);
					else
						_errorLogger.LogError($"When calculating the snapshotted path for file '{subPath}', it was supposed to be within mount '{_tracker.Snapshot.MountPoint}'", ErrorLogger.Summary.InternalError);

					_snapshottedPath = System.IO.Path.Combine(_tracker?.Snapshot.BuildPath() ?? "", subPath.TrimStart('/'));
				}

				try
				{
					if (File.Exists(_snapshottedPath))
						_snapshottedFileSize = new FileInfo(_snapshottedPath).Length;
				}
				catch {}
			}

			return _snapshottedPath;
		}

		public long FileSize { get { ResolveSnapshottedPath(); return _snapshottedFileSize; } }

		public string Path => _path;
		public string SnapshottedPath => ResolveSnapshottedPath();

		public override string ToString()
		{
			ResolveSnapshottedPath();

			return _path + " (" + _snapshottedFileSize + " byte snapshot at: " + _snapshottedPath + ")";
		}
	}
}

