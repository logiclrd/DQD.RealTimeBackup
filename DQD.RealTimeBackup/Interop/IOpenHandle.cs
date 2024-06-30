using System;

namespace DQD.RealTimeBackup.Interop
{
	public interface IOpenHandle : IDisposable
	{
		string ReadLink();
	}
}

