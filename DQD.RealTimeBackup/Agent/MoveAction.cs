namespace DQD.RealTimeBackup.Agent
{
	public class MoveAction : BackupAction
	{
		public string FromPath;
		public string ToPath;

		public MoveAction(string fromPath, string toPath)
		{
			this.FromPath = fromPath;
			this.ToPath = toPath;
		}

		public override string ToString()
		{
			return "MOVE: from " + FromPath + " to " + ToPath;
		}
	}
}
