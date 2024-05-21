namespace DeltaQ.RTB.Interop
{
	public class Mount : IMount
	{
		int _mountID;
		int _parentMountID;
		int _deviceMajor;
		int _deviceMinor;
		string _root;
		string _mountPoint;
		string _options;
		string[] _optionalFields;
		string _fileSystemType;
		string _deviceName;
		string? _superblockOptions;

		public int MountID => _mountID;
		public int ParentMountID => _parentMountID;
		public int DeviceMajor => _deviceMajor;
		public int DeviceMinor => _deviceMinor;
		public string Root => _root;
		public string MountPoint => _mountPoint;
		public string Options => _options;
		public string[] OptionalFields => _optionalFields;
		public string FileSystemType => _fileSystemType;
		public string DeviceName => _deviceName;
		public string? SuperblockOptions => _superblockOptions;

		public Mount(int mountID, int parentMountID, int deviceMajor, int deviceMinor, string root, string mountPoint, string options, string[] optionalFields, string fileSystemType, string deviceName, string? superblockOptions)
		{
			_mountID = mountID;
			_parentMountID = parentMountID;
			_deviceMajor = deviceMajor;
			_deviceMinor = deviceMinor;
			_root = root;
			_mountPoint = mountPoint;
			_options = options;
			_optionalFields =  optionalFields;
			_fileSystemType = fileSystemType;
			_deviceName = deviceName;
			_superblockOptions = superblockOptions;
		}

		public bool TestDeviceAccess()
		{
			if (_deviceName != null)
				return (NativeMethods.access(_deviceName, NativeMethods.F_OK) == 0);
			else
				return false;
		}
	}
}
