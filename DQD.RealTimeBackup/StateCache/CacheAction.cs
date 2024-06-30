using System;
using System.IO;

namespace DQD.RealTimeBackup.StateCache
{
	public class CacheAction
	{
		public CacheActionType CacheActionType;
		public string? SourcePath;
		public string? Path;

		static void EncodeString(TextWriter writer, string? str)
		{
			if (str == null)
				writer.WriteLine('N');
			else
			{
				writer.Write('V');
				writer.WriteLine(str);
			}
		}

		static string? DecodeString(string? encoded)
		{
			if (encoded == null)
				return null;
			else if (encoded.StartsWith('V'))
				return encoded.Substring(1);
			else if (encoded.StartsWith('N'))
				return null;
			else
				return encoded;
		}

		public string Serialize()
		{
			var buffer = new StringWriter();

			buffer.WriteLine(CacheActionType);

			EncodeString(buffer, Path);
			EncodeString(buffer, SourcePath);

			return buffer.ToString();
		}

		public static CacheAction Deserialize(string text)
		{
			var buffer = new StringReader(text);

			var ret = new CacheAction();

			Enum.TryParse(
				typeof(CacheActionType),
				buffer.ReadLine(),
				out var parsed);

			if (parsed is CacheActionType cacheActionType)
				ret.CacheActionType = cacheActionType;

			ret.Path = DecodeString(buffer.ReadLine());
			ret.SourcePath = DecodeString(buffer.ReadLine());

			return ret;
		}

		public string? ActionFileName;
		public bool IsComplete;

		public static CacheAction UploadFile(string sourcePath, string destinationPath)
			=> new CacheAction() { CacheActionType = CacheActionType.UploadFile, SourcePath = sourcePath, Path = destinationPath };
		public static CacheAction DeleteFile(string path)
			=> new CacheAction() { CacheActionType = CacheActionType.DeleteFile, Path = path };
	}
}
