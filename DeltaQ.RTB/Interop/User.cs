namespace DeltaQ.RTB.Interop
{
	public class User : IUser
	{
		public string UserName { get; set; } = "";
		public int UserID { get; set; }
		public int GroupID { get; set; }
		public string RealName { get; set; } = "";
		public string Contact { get; set; } = "";
		public string OfficePhoneNumber { get; set; } = "";
		public string HomePhoneNumber { get; set; } = "";
		public string OtherContact { get; set; } = "";
		public string HomePath { get; set; } = "";
		public string LoginShell { get; set; } = "";

		public void LoadFromGecosField(string gecosField)
		{
			string[] parts = gecosField.Split(',', 5);

			RealName = parts[0];
			Contact = (parts.Length > 1) ? parts[1] : "";
			OfficePhoneNumber = (parts.Length > 2) ? parts[2] : "";
			HomePhoneNumber = (parts.Length > 3) ? parts[3] : "";
			OtherContact = (parts.Length > 4) ? parts[4] : "";
		}

		public void LoadFromPasswdLine(string line)
		{
			string[] parts = line.Split(':', 7);

			UserName = parts[0];

			// Ignore the password field. It's a dummy anyway on anything newer than the 1980s.

			if ((parts.Length > 2) && int.TryParse(parts[2], out var userID))
				UserID = userID;
			if ((parts.Length > 3) && int.TryParse(parts[3], out var groupID))
				GroupID = groupID;
			
			if (parts.Length > 4)
				LoadFromGecosField(parts[4]);

			HomePath = (parts.Length > 5) ? parts[5] : "";
			LoginShell = (parts.Length > 6) ? parts[6] : "";
		}
	}
}
