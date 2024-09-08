using System;

namespace DQD.RealTimeBackup.StateCache
{
	public class BatchFileInfo : IComparable<BatchFileInfo>
	{
		public int BatchNumber;
		public string? Path;
		public long FileSize;

		public int CompareTo(BatchFileInfo? other)
		{
			return BatchNumber.CompareTo(other?.BatchNumber ?? -1);
		}
	}
}
