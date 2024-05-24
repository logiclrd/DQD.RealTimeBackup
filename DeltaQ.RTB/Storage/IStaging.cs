using System;
using System.IO;

namespace DeltaQ.RTB.Storage
{
	public interface IStaging
	{
		IStagedFile StageFile(string path);
		IStagedFile StageFile(Stream data);
	}
}

