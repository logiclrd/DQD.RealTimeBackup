using System;
using System.Runtime.CompilerServices;

using DeltaQ.RTB.Diagnostics;

namespace DeltaQ.RTB.FileSystem
{
	public class SnapshotReference : IDisposable
	{
		SnapshotReferenceTracker? _tracker;
		string _path;

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

		public string Path => _path;
		public string SnapshottedPath
		{
			get
			{
				if (_tracker!.Snapshot.MountPoint == "/")
					return System.IO.Path.Combine(_tracker?.Snapshot.BuildPath() ?? "", _path.TrimStart('/'));
				else
				{
					string subPath = _path;

					if (subPath.StartsWith(_tracker.Snapshot.MountPoint)
					 && (subPath[_tracker.Snapshot.MountPoint.Length] == '/'))
						subPath = subPath.Substring(_tracker.Snapshot.MountPoint.Length + 1);
					else
						_errorLogger.LogError($"When calculating the snapshotted path for file '{subPath}', it was supposed to be within mount '{_tracker.Snapshot.MountPoint}'", ErrorLogger.Summary.InternalError);

					return System.IO.Path.Combine(_tracker?.Snapshot.BuildPath() ?? "", subPath.TrimStart('/'));
				}
			}
		}
	}
}

