using System;
using System.IO;

namespace DeltaQ.RTB.FileSystem
{
	class ZFSDebugLog
	{
		static bool s_isEnabled = false;
		static string s_zfsDebugLogPath = "disabled";
		static object s_sync = new object();

		public static void Enable(string path)
		{
			s_isEnabled = true;
			s_zfsDebugLogPath = path;
		}

		public static void WriteLine(string line)
		{
			if (s_isEnabled)
			{
				lock (s_sync)
				{
					using (var writer = new StreamWriter(s_zfsDebugLogPath, append: true))
						writer.WriteLine(line);
				}
			}
		}

		public static void WriteLine(string format, params object?[] args)
		{
			WriteLine(string.Format(format, args));
		}

		public static void WriteLine(object? value = null)
		{
			WriteLine(value?.ToString() ?? "");
		}
	}
}
