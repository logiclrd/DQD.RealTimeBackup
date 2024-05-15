using System;
using System.IO;

namespace DeltaQ.RTB.Storage
{
	public class Staging : IStaging
	{
		public IStagedFile StageFile(string path)
			=> throw new NotImplementedException("TODO");

		public IStagedFile StageFile(Stream path)
			=> throw new NotImplementedException("TODO");
	}
}

