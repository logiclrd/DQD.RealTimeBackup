using System.Collections.Generic;

namespace DeltaQ.RTB.Interop
{
	public class UserList : IUserList
	{
		IPasswdProvider _passwd;

		public UserList(IPasswdProvider passwd)
		{
			_passwd = passwd;
		}

		public IEnumerable<IUser> EnumerateUsers()
		{
			using (var reader = _passwd.OpenRead())
			{
				while (true)
				{
					var line = reader.ReadLine();

					if (line == null)
						break;

					var user = new User();

					user.LoadFromPasswdLine(line);

					yield return user;
				}
			}
		}

		public IEnumerable<IUser> EnumerateRealUsers()
		{
			foreach (var user in EnumerateUsers())
			{
				if (user.LoginShell.EndsWith("/nologin"))
					continue;
				if (user.LoginShell.EndsWith("/false"))
					continue;
				if (user.LoginShell.EndsWith("/sync"))
					continue;

				yield return user;
			}
		}
	}
}
