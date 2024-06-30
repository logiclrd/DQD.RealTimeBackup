using System;

namespace DQD.RealTimeBackup.Storage
{
	public interface IStagedFile : IDisposable
	{
		string Path { get; }
	}
}

