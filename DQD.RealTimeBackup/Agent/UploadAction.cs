using DQD.RealTimeBackup.FileSystem;

namespace DQD.RealTimeBackup.Agent
{
	public class UploadAction : BackupAction
	{
		public SnapshotReference Source;
		public string ToPath;

		public UploadAction(SnapshotReference source, string toPath)
		{
			this.Source = source;
			this.ToPath = toPath;
		}

		public override void Dispose()
		{
			Source.Dispose();
		}
	}
}
