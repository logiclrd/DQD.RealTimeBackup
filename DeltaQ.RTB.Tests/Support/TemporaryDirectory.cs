using System;
using System.IO;

namespace DeltaQ.RTB.Tests.Support
{
	class TemporaryDirectory : IDisposable
	{
		string _path;
		bool _disposed;

		static Random s_rnd = new Random();

		public string Path => _path;

		public TemporaryDirectory()
			: this("/tmp/" + DateTime.UtcNow.Ticks + "-" + s_rnd.NextInt64())
		{
		}

		public TemporaryDirectory(string path, bool attachToExisting = false)
		{
			if (!attachToExisting && Directory.Exists(path))
				throw new Exception("The path passed to TemporaryDirectory already exists. Halting execution because otherwise we will delete the subtree on Dispose.");

			_path = path;

			Directory.CreateDirectory(path);
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				Directory.Delete(_path, recursive: true);
				_disposed = true;
			}
		}
	}
}
