using System;
using System.Runtime.InteropServices;

namespace DeltaQ.RTB.Tests.Support
{
	class TestsNativeMethods
	{
		[DllImport("c", SetLastError = true)]
		public static extern int geteuid();
	}
}

