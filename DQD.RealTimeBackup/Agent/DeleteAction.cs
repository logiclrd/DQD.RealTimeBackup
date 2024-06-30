namespace DQD.RealTimeBackup.Agent
{
	public class DeleteAction : BackupAction
	{
		public string Path;

		public DeleteAction(string path)
		{
			this.Path = path;
		}
	}
}
