using System;

namespace DQD.RealTimeBackup.Agent
{
	public abstract class BackupAction : IDisposable
	{
		public virtual void Dispose() {}
	}
}
