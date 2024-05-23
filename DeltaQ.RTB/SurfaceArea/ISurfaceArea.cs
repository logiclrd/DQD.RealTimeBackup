using System;
using System.Collections.Generic;

using DeltaQ.RTB.Interop;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.SurfaceArea
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
