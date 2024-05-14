using System;

namespace DeltaQ.RTB.Storage
{
	public interface IStagedFile : IDisposable
	{
		string Path { get; }
	}
}

