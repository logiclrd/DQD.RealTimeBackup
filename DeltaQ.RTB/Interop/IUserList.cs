using System;
using System.Collections.Generic;

namespace DeltaQ.RTB.Interop
{
	public interface IUserList
	{
		IEnumerable<IUser> EnumerateUsers();
		IEnumerable<IUser> EnumerateRealUsers();
	}
}
