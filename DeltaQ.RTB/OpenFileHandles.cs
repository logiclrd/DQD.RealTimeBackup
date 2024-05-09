using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

public class OpenFileHandles : IOpenFileHandles
{
  const string LSOFPath = "/usr/bin/lsof";

  public IEnumerable<OpenFileHandle> Enumerate(string path)
  {
    var psi = new ProcessStartInfo();

    psi.FileName = LSOFPath;
    psi.Arguments = "-l -F -w -- \"" + path + "\"";
    psi.RedirectStandardOutput = true;

    using (var process = Process.Start(psi)!)
    {
      OpenFileHandle? handle = null;

      while (true)
      {
        string? line = process.StandardOutput!.ReadLine();

        if (line == null)
          break;

        if (line.Length == 0)
          continue;

        if (line[0] == 'f')
        {
          if (handle != null)
            yield return handle;

          handle = new OpenFileHandle();
        }

        if (handle != null)
        {
          char field = line[0];
          string fieldValue = line.Substring(1);

          switch (field)
          {
            case 'p': handle.ProcessID = int.Parse(fieldValue); break;
            case 'g': handle.ProcessGroupID = int.Parse(fieldValue); break;
            case 'R': handle.ParentProcessID = int.Parse(fieldValue); break;
            case 'c': handle.CommandName = fieldValue; break;
            case 'u': handle.ProcessUserID = int.Parse(fieldValue); break;
            case 'L': handle.ProcessLoginName = fieldValue; break;
            case 'f': handle.FileDescriptor = int.Parse(fieldValue); break;
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

