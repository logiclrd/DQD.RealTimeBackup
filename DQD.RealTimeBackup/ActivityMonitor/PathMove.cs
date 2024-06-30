namespace DQD.RealTimeBackup.ActivityMonitor
{
	public class PathMove
	{
		public string PathFrom;
		public string PathTo;

		public PathMove() { PathFrom = PathTo = ""; }

		public PathMove(string pathFrom, string pathTo)
		{
			PathFrom = pathFrom;
			PathTo = pathTo;
		}
	}
}

