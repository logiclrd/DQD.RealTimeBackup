using System;
using System.ComponentModel;

namespace DQD.RealTimeBackup.Interop
{
	public struct FileAccessNotifyFlags
	{
		int _value;

		public FileAccessNotifyFlags(int value)
		{
			_value = value;
		}

		public class Class
		{
			int _value;

			private Class(int value)
			{
				_value = value;
			}

			[Description("FAN_CLASS_NOTIF")] public static Class Notification
				=> new Class(0);
			[Description("FAN_CLASS_CONTENT")] public static Class Content
				=> new Class(4);
			[Description("FAN_CLASS_PRE_CONTENT")] public static Class PreContent
				=> new Class(8);

			public static FileAccessNotifyFlags operator |(Class left, Report right)
			{
				return new FileAccessNotifyFlags(left._value | (int)right);
			}
		}

		[Flags]
		public enum Report
		{
			[Description("FAN_REPORT_PIDFD")]      ProcessFileDescriptor = 0x00000080,
			[Description("FAN_REPORT_TID")]        ThreadID              = 0x00000100,
			[Description("FAN_REPORT_FID")]        UniqueFileID          = 0x00000200,
			[Description("FAN_REPORT_DIR_FID")]    UniqueDirectoryID     = 0x00000400,
			[Description("FAN_REPORT_NAME")]       IncludeName           = 0x00000800,
			[Description("FAN_REPORT_TARGET_FID")] TargetFileID          = 0x00001000,
		}

		public static FileAccessNotifyFlags operator |(FileAccessNotifyFlags left, Report right)
		{
			return new FileAccessNotifyFlags(left._value | (int)right);
		}
	}
}
