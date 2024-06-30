using System;
using System.IO;

namespace DQD.RealTimeBackup.Storage
{
	public interface IStaging
	{
		IStagedFile StageFile(string path);
		IStagedFile StageFile(Stream data);
	}
}

