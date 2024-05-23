using System;
using System.Collections.Generic;

using DeltaQ.RTB.Interop;

namespace DeltaQ.RTB.SurfaceArea
{
	public interface ISurfaceArea
	{
		event EventHandler<string>? DiagnosticOutput;

		IEnumerable<IMount> Mounts { get; }

		void ClearMounts();
		void AddMount(IMount mount);
		void AddMounts(IEnumerable<IMount> mounts);

		void BuildDefault();
	}
}
