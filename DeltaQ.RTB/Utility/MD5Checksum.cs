using System;
using System.IO;
using System.Security.Cryptography;

namespace DeltaQ.RTB.Utility
{
	public class MD5Checksum : IChecksum
	{
		MD5 _md5 = MD5.Create();

		public string ComputeChecksum(Stream stream)
		{
			var checksumBytes = _md5.ComputeHash(stream);

			char[] checksumChars = new char[checksumBytes.Length * 2];

			for (int i = 0; i < checksumBytes.Length; i++)
			{
				byte b = checksumBytes[i];

				checksumChars[i + i] = "0123456789abcdef"[b >> 4];
				checksumChars[i + i + 1] = "0123456789abcdef"[b & 15];
			}

			return new string(checksumChars);
		}

		public string ComputeChecksum(string path)
		{
			using (var stream = File.OpenRead(path))
				return ComputeChecksum(stream);
		}
	}
}
