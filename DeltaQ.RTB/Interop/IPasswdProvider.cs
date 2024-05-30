using System.IO;

namespace DeltaQ.RTB.Interop
{
	public interface IPasswdProvider
	{
		TextReader OpenRead();
	}
}
