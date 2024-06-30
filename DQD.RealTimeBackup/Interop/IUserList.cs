using System;
using System.Collections.Generic;

namespace DQD.RealTimeBackup.Interop
{
	public interface IUserList
	{
		IEnumerable<IUser> EnumerateUsers();
		IEnumerable<IUser> EnumerateRealUsers();
	}
}
