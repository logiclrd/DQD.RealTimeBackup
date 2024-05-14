using System;

namespace DeltaQ.RTB.Interop
{
	public interface IOpenHandle : IDisposable
	{
		string ReadLink();
	}
}

