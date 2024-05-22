using System;

namespace DeltaQ.RTB.Agent
{
	public abstract class BackupAction : IDisposable
	{
		public virtual void Dispose() {}
	}
}
