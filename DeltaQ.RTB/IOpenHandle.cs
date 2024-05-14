using System;

namespace DeltaQ.RTB
{
	public interface IOpenHandle : IDisposable
	{
		string ReadLink();
	}
}

