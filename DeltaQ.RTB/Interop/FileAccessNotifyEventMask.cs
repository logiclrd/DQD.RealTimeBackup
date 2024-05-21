using System;
using System.ComponentModel;

namespace DeltaQ.RTB.Interop
{
	[Flags]
	public enum FileAccessNotifyEventMask : long
	{
		[Description("FAN_ACCESS")] Access                          = 0x0001,
		[Description("FAN_MODIFY")] Modified                        = 0x0002,
		[Description("FAN_ATTRIB")] AttributeChange                 = 0x0004,
		[Description("FAN_CLOSE_WRITE")] ClosedAfterWriting         = 0x0008,
		[Description("FAN_CLOSE_NOWRITE")] ClosedAfterReading       = 0x0010,
		[Description("FAN_OPEN")] Open                              = 0x0020,
		[Description("FAN_MOVED_FROM")] ChildMovedOut               = 0x0040,
		[Description("FAN_MOVED_TO")] ChildMovedIn                  = 0x0080,
		[Description("FAN_CREATE")] ChildCreated                    = 0x0100,
		[Description("FAN_DELETE")] ChildDeleted                    = 0x0200,
		[Description("FAN_DELETE_SELF")] Deleted                    = 0x0400,
		[Description("FAN_MOVE_SELF")] Moved                        = 0x0800,
		[Description("FAN_OPEN_EXEC")] OpenForExecute               = 0x1000,

		[Description("FAN_Q_OVERFLOW")] LostEvents                  = 0x4000,
		[Description("FAN_FS_ERROR")] FileSystemError               = 0x8000,
		
		[Description("FAN_OPEN_OPEN")] OpenPermissionCheck          = 0x10000,
		[Description("FAN_ACCESS_PERM")] ReadPermissionCheck        = 0x20000,
		[Description("FAN_OPEN_EXEC_PERM")] ExecutePermissionCheck  = 0x40000,

		[Description("FAN_RENAME")] ChildMoved                      = 0x10000000,

		[Description("FAN_ONDIR")] Flag_SubjectIsDirectory          = 0x40000000,
	}
}

