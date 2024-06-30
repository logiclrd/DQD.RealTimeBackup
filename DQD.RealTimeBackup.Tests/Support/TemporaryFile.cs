using System;
using System.IO;

namespace DQD.RealTimeBackup.Tests.Support
{
	class TemporaryFile : IDisposable
	{
		string _path;
		bool _disposed;

		static Random s_rnd = new Random();

		public string Path => _path;

		public TemporaryFile()
			: this("/tmp/" + DateTime.UtcNow.Ticks + "-" + s_rnd.NextInt64())
		{
		}

		public TemporaryFile(string path)
		{
			_path = path;

			File.Open(path, FileMode.OpenOrCreate).Dispose();
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				File.Delete(_path);
				_disposed = true;
			}
		}
	}
}
