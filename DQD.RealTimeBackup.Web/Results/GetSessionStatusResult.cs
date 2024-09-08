using System.Threading.Tasks;

namespace DQD.RealTimeBackup.Web
{
	public class GetSessionStatusResult
	{
		public bool LoadFileStateComplete { get; set; }
		public double LoadFileStateProgress { get; set; }
		public int FileCount { get; set; }

		public static GetSessionStatusResult FromSession(Session session)
		{
			var result = new GetSessionStatusResult();

			result.LoadFileStateComplete = (session.LoadFileStateTask == null) || (session.LoadFileStateTask.Status >= TaskStatus.RanToCompletion);
			result.LoadFileStateProgress = session.LoadFileStateProgress;
			result.FileCount = session.GetFileCount();

			return result;
		}
	}
}
