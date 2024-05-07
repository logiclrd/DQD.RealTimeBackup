using System;
using System.IO;

public class OpenFileHandle
{
  public int ProcessID;
  public int ProcessGroupID;
  public int ParentProcessID;
  public string? CommandName;
  public int ProcessUserID;
  public string? ProcessLoginName;
  public int FileDescriptor;
  public FileAccess FileAccess;
  public string? LockStatus;
  public FileTypes FileType;
  public int Flags;
  public int DeviceNumber;
  public long FileSize;
  public long INode;
  public int LinkCount;
  public string? FileName;
}

