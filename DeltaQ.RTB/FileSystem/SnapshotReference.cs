using System;
using System.Runtime.CompilerServices;

namespace DeltaQ.RTB.FileSystem
{
	public class SnapshotReference : IDisposable
	{
		SnapshotReferenceTracker? _tracker;
		string _path;

		public SnapshotReference(SnapshotReferenceTracker tracker, string path)
		{
			_tracker = tracker;
			_path = path;
		}

		public void Dispose()
		{
			_tracker?.Release();
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
						Console.Error.WriteLine("WARNING: File '{0}' is supposed to be within mount '{1}'", subPath, _tracker.Snapshot.MountPoint);

					return System.IO.Path.Combine(_tracker?.Snapshot.BuildPath() ?? "", subPath.TrimStart('/'));
				}
			}
		}
	}
}

