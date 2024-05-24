using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

using DeltaQ.RTB.ActivityMonitor;

namespace DeltaQ.RTB.Interop
{
	public class OpenFileHandles : IOpenFileHandles
	{
		OperatingParameters _parameters;

		public OpenFileHandles(OperatingParameters parameters)
		{
			_parameters = parameters;
		}

		public IEnumerable<OpenFileHandle> EnumerateAll()
			=> RunLSOFCommandAndParseOutput("-l -F -w");
		public IEnumerable<OpenFileHandle> Enumerate(string path)
			=> RunLSOFCommandAndParseOutput("-l -F -w -- \"" + path + "\"");

		IEnumerable<OpenFileHandle> RunLSOFCommandAndParseOutput(string arguments)
		{
			var psi = new ProcessStartInfo();

			psi.FileName = _parameters.LSOFBinaryPath;
			psi.Arguments = arguments;
			psi.RedirectStandardOutput = true;

			using (var process = Process.Start(psi)!)
			{
				int processID = -1;
				int processGroupID = -1;
				int parentProcessID = -1;
				string? commandName = null;
				int processUserID = -1;
				string? processLoginName = null;

				OpenFileHandle? handle = null;

				while (true)
				{
					string? line = process.StandardOutput!.ReadLine();

					if (line == null)
						break;

					if (line.Length == 0)
						continue;

					// Start of new Process Set
					if (line[0] == 'p')
					{
						processID = -1;
						processGroupID = -1;
						parentProcessID = -1;
						commandName = null;
						processUserID = -1;
						processLoginName = null;
					}

					if (line[0] == 'f')
					{
						if ((handle != null) && (handle.FileDescriptor >= 0))
							yield return handle;

						handle = new OpenFileHandle();

						handle.ProcessID = processID;
						handle.ProcessGroupID = processGroupID;
						handle.ParentProcessID = parentProcessID;
						handle.CommandName = commandName;
						handle.ProcessUserID = processUserID;
						handle.ProcessLoginName = processLoginName;
					}

					char field = line[0];
					string fieldValue = line.Substring(1);

					// Process Set fields
					switch (field)
					{
						case 'p': processID = int.Parse(fieldValue); break;
						case 'g': processGroupID = int.Parse(fieldValue); break;
						case 'R': parentProcessID = int.Parse(fieldValue); break;
						case 'c': commandName = fieldValue; break;
						case 'u': processUserID = int.Parse(fieldValue); break;
						case 'L': processLoginName = fieldValue; break;
					}

					if (handle != null)
					{
						// File Set fields
						switch (field)
						{
							case 'f':
								if (!int.TryParse(fieldValue, out var fd))
									fd = -1;

								handle.FileDescriptor = fd;

								break;
							case 'a':
								handle.FileAccess =
									(fieldValue.Contains("r") ? FileAccess.Read : default) |
									(fieldValue.Contains("w") ? FileAccess.Write : default);
								break;
							case 'l': handle.LockStatus = fieldValue; break;
							case 't': handle.FileType = FileTypesUtility.Parse(fieldValue); break;
							case 'G': handle.Flags = Convert.ToInt32(fieldValue.Split(';')[0], fromBase: 16); break;
							case 'D': handle.DeviceNumber = Convert.ToInt32(fieldValue, fromBase: 16); break;
							case 's': handle.FileSize = long.Parse(fieldValue); break;
							case 'i': handle.INode = long.Parse(fieldValue); break;
							case 'k': handle.LinkCount = int.Parse(fieldValue); break;
							case 'n': handle.FileName = fieldValue; break;
						}
					}
				}

				if (handle != null)
					yield return handle;
			}
		}
	}
}

