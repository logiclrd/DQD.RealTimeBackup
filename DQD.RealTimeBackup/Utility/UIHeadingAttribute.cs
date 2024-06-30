using System;

namespace DQD.RealTimeBackup.Utility
{
	[AttributeUsage(AttributeTargets.Field)]
	public class UIHeadingAttribute : Attribute
	{
		string _text;

		public string Text => _text;

		public UIHeadingAttribute(string text)
		{
			_text = text;
		}
	}
}
