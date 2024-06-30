using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DQD.RealTimeBackup.Interop
{
	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	public struct TimeSpec
	{
		[Description("tv_sec")] public long SecondsSinceEpoch;
		[Description("tv_nsec")] public long NanosecondsSinceSecondsSinceEpoch;

		public DateTime ToDateTime()
		{
			var epoch = new DateTime(1970, 1, 1);

			return epoch
				.AddSeconds(SecondsSinceEpoch)
				.AddMicroseconds(NanosecondsSinceSecondsSinceEpoch / 1000.0);
		}
	}
}
