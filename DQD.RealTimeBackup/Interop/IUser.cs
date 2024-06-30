namespace DQD.RealTimeBackup.Interop
{
	public interface IUser
	{
		string UserName { get; }
		int UserID { get; }
		int GroupID { get; }
		string RealName { get; }
		string Contact { get; }
		string OfficePhoneNumber { get; }
		string HomePhoneNumber { get; }
		string OtherContact { get; }
		string HomePath { get; }
		string LoginShell { get; }
	}
}
