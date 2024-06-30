using System;
using System.IO;
using System.Linq;

using NUnit.Framework;

using NSubstitute;

using FluentAssertions;

using DQD.RealTimeBackup.Interop;

namespace DQD.RealTimeBackup.Tests.Fixtures.Interop
{
	[TestFixture]
	public class UserListTests
	{
		const string DummyPasswdFile =
@"root:x:0:0:root:/root:/bin/bash
logiclrd:x:1000:1000:Jonathan Gilbert,,,:/home/logiclrd:/bin/bash
gecostest:x:1001:1001:RN,Cont,1-234,1-345,Donkey,Mule:/home/gecostest:/bin/tcsh
daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin
bin:x:2:2:bin:/bin:/usr/sbin/nologin
sys:x:3:3:sys:/dev:/usr/sbin/nologin
sync:x:4:65534:sync:/bin:/bin/sync
games:x:5:60:games:/usr/games:/usr/sbin/nologin
man:x:6:12:man:/var/cache/man:/usr/sbin/nologin
gdm:x:128:134:Gnome Display Manager:/var/lib/gdm3:/bin/false
sshd:x:129:65534::/run/sshd:/usr/sbin/nologin";

		User[] DummyRealUsers =
			new[]
			{
				new User() { UserName = "root", UserID = 0, GroupID = 0, RealName = "root", HomePath = "/root", LoginShell = "/bin/bash" },
				new User() { UserName = "logiclrd", UserID = 1000, GroupID = 1000, RealName = "Jonathan Gilbert", HomePath = "/home/logiclrd", LoginShell = "/bin/bash" },
				new User() { UserName = "gecostest", UserID = 1001, GroupID = 1001, RealName = "RN", Contact = "Cont", OfficePhoneNumber = "1-234", HomePhoneNumber = "1-345", OtherContact = "Donkey,Mule", HomePath = "/home/gecostest", LoginShell = "/bin/tcsh" },
			};

		User[] DummyFakeUsers =
			new[]
			{
				new User() { UserName = "daemon", UserID = 1, GroupID = 1, RealName = "daemon", HomePath = "/usr/sbin", LoginShell = "/usr/sbin/nologin" },
				new User() { UserName = "bin", UserID = 2, GroupID = 2, RealName = "bin", HomePath = "/bin", LoginShell = "/usr/sbin/nologin" },
				new User() { UserName = "sys", UserID = 3, GroupID = 3, RealName = "sys", HomePath = "/dev", LoginShell = "/usr/sbin/nologin" },
				new User() { UserName = "sync", UserID = 4, GroupID = 65534, RealName = "sync", HomePath = "/bin", LoginShell = "/bin/sync" },
				new User() { UserName = "games", UserID = 5, GroupID = 60, RealName = "games", HomePath = "/usr/games", LoginShell = "/usr/sbin/nologin" },
				new User() { UserName = "man", UserID = 6, GroupID = 12, RealName = "man", HomePath = "/var/cache/man", LoginShell = "/usr/sbin/nologin" },
				new User() { UserName = "gdm", UserID = 128, GroupID = 134, RealName = "Gnome Display Manager", HomePath = "/var/lib/gdm3", LoginShell = "/bin/false" },
				new User() { UserName = "sshd", UserID = 129, GroupID = 65534, RealName = "", HomePath = "/run/sshd", LoginShell = "/usr/sbin/nologin" },
			};

		[Test]
		public void EnumerateUsers_should_enumerate_all_users()
		{
			// Arrange
			var passwdProvider = Substitute.For<IPasswdProvider>();

			passwdProvider.OpenRead().Returns(new StringReader(DummyPasswdFile));

			var sut = new UserList(passwdProvider);

			// Act
			var users = sut.EnumerateUsers().ToList();

			// Assert
			users.Should().BeEquivalentTo(DummyRealUsers.Concat(DummyFakeUsers));
		}

		[Test]
		public void EnumerateRealUsers_should_enumerate_users_with_real_shells()
		{
			// Arrange
			var passwdProvider = Substitute.For<IPasswdProvider>();

			passwdProvider.OpenRead().Returns(new StringReader(DummyPasswdFile));

			var sut = new UserList(passwdProvider);

			// Act
			var users = sut.EnumerateRealUsers().ToList();

			// Assert
			users.Should().BeEquivalentTo(DummyRealUsers);
		}
	}
}
