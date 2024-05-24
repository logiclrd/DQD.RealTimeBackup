using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeltaQ.RTB.Interop
{
	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	public struct StatInfo
	{
		[Description("st_dev")] public long ContainerDeviceID;
		[Description("st_ino")] public long InodeNumber;
		[Description("st_nlink")] public long NumberOfHardLinks;
		[Description("st_mode")] public int Mode;
		[Description("st_uid")] public int UserID;
		[Description("st_gid")] public int GroupID;
		[Description("st_rdev")] public long DeviceID;
		[Description("st_size")] public long FileSize;
		[Description("st_blksize")] public long BlockSize;
		[Description("st_blocks")] public long BlockCount;

		[Description("st_atim")] public TimeSpec LastAccessTime;
		[Description("st_mtim")] public TimeSpec LastWriteTime;
		[Description("st_ctim")] public TimeSpec LastChangeTime;

		[EditorBrowsable(EditorBrowsableState.Never)] public long Padding1; 
		[EditorBrowsable(EditorBrowsableState.Never)] public long Padding2; 
		[EditorBrowsable(EditorBrowsableState.Never)] public long Padding3; 
	}
}
