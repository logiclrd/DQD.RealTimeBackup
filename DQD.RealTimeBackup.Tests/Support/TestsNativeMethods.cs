using System;
using System.Runtime.InteropServices;

namespace DQD.RealTimeBackup.Tests.Support
{
	class TestsNativeMethods
	{
		[DllImport("c", SetLastError = true)]
		public static extern int geteuid();
	}
}

