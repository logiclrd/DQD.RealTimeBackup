using System;
using System.IO;

namespace DeltaQ.RTB.Storage
{
	public class Staging : IStaging
	{
		public IStagedFile StageFile(string path)
		{
			using (var stream = File.OpenRead(path))
				return StageFile(stream);
		}
		
		public IStagedFile StageFile(Stream data)
		{
			return new StagedFile(data);
		}
	}
}

