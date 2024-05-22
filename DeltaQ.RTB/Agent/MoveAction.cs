namespace DeltaQ.RTB.Agent
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
	}
}
