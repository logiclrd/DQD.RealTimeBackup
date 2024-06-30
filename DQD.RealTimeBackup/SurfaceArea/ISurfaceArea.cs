using System;
using System.Collections.Generic;

using DQD.RealTimeBackup.Interop;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.SurfaceArea
{
	public interface ISurfaceArea : IDiagnosticOutput
	{
		IEnumerable<IMount> Mounts { get; }

		void ClearMounts();
		void AddMount(IMount mount);
		void AddMounts(IEnumerable<IMount> mounts);

		void BuildDefault();
	}
}
