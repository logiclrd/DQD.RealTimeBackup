using System.ComponentModel;

namespace DeltaQ.RTB.Interop
{
	public enum FileAccessNotifyEventInfoType : byte
	{
		[Description("FAN_EVENT_INFO_TYPE_FID")]       FileIdentifier                   = 1,
		[Description("FAN_EVENT_INFO_TYPE_DFID_NAME")] ContainerIdentifierAndFileName   = 2,
		[Description("FAN_EVENT_INFO_TYPE_DFID")]      ContainerIdentifier              = 3,
		[Description("FAN_EVENT_INFO_TYPE_PIDFD")]     ProcessFileDescriptor            = 4,
		[Description("FAN_EVENT_INFO_TYPE_ERROR")]     Error                            = 5,

		[Description("FAN_EVENT_INFO_TYPE_OLD_DFID_NAME")] ContainerIdentifierAndFileName_From = 10,
		[Description("FAN_EVENT_INFO_TYPE_NEW_DFID_NAME")] ContainerIdentifierAndFileName_To   = 12,
	}
}
