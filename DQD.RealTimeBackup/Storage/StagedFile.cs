using System;
using System.IO;

namespace DQD.RealTimeBackup.Storage
{
	public class StagedFile : IStagedFile
	{
		string _path;

		public string Path => _path;

		public StagedFile(Stream data)
		{
			_path = System.IO.Path.GetTempFileName();

			using (var stagedFileStream = File.OpenWrite(_path))
			{
				data.Position = 0;
				data.CopyTo(stagedFileStream);
			}
		}

		public void Dispose()
		{
			File.Delete(_path);
		}
	}
}
