using System;

namespace DQD.RealTimeBackup.Interop
{
	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	public class FileTypeStringAttribute : Attribute
	{
		string _tag;

		public FileTypeStringAttribute(string tag)
		{
			_tag = tag;
		}

		public string Tag => _tag;
	}
}
