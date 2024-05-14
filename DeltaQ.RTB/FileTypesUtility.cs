using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace DeltaQ.RTB
{
	public class FileTypesUtility
	{
		static Dictionary<string, FileTypes> s_fileTypesByTags =
			typeof(FileTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
			.Select(f => (f.GetCustomAttributes(typeof(FileTypeStringAttribute)).OfType<FileTypeStringAttribute>().Single(), f))
			.Select(p => (p.Item1.Tag, (FileTypes)p.Item2.GetValue(null)!))
			.ToDictionary(keySelector: p => p.Item1, elementSelector: p => p.Item2);

		public static FileTypes Parse(string tag)
		{
			if (s_fileTypesByTags.TryGetValue(tag, out var fileType))
				return fileType;
			else
				return FileTypes.Unknown;
		}
	}
}

