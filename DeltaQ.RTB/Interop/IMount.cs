namespace DeltaQ.RTB.Interop
{
	public interface IMount
	{
		string DeviceName { get; }
		string MountPoint { get; }
		string? Type { get; }
		string? Options { get; }
		int Frequency { get; }
		int PassNumber { get; }

		bool TestDeviceAccess();
	}
}
