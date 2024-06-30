using System;

namespace DQD.RealTimeBackup.Bridge.Serialization
{
	[AttributeUsage(AttributeTargets.Field)]
	public class FieldOrderAttribute : Attribute
	{
		int _order;

		public int Order => _order;

		public FieldOrderAttribute(int order)
		{
			_order = order;
		}
	}
}
