using System;
using System.IO;
using System.Threading;

namespace DeltaQ.RTB.Utility
{
	public class FileUtility
	{
		static internal bool FilesAreEqual(string path1, string path2, CancellationToken token)
		{
			try
			{
				using (var file1 = File.OpenRead(path1))
				using (var file2 = File.OpenRead(path2))
				{
					if (file1.Length != file2.Length)
						return false;

					long length = file1.Length;

					int bufferSize = (int)Math.Min(1048576, length);

					byte[] buffer1 = new byte[bufferSize];
					byte[] buffer2 = new byte[bufferSize];

					bool BuffersAreEqual(int size)
					{
						return buffer1.AsSpan(0, size).SequenceEqual(buffer2.AsSpan(0, size));
					}

					// Heuristic: The file is most likely to be modified close to the beginning or close to the end. So,
					// compare the last megabyte first, then go back and do the rest of the file.

					long mark = length - bufferSize;

					file1.Position = mark;
					file2.Position = mark;

					if (!ReadFully(file1, buffer1, bufferSize)
					 || !ReadFully(file2, buffer2, bufferSize))
						 return false;

					if (!BuffersAreEqual(bufferSize))
						return false;

					// Now start over from the beginning of the file.
					long remaining = length - bufferSize;

					file1.Position = 0;
					file2.Position = 0;

					while (remaining > 0)
					{
						int readSize = (int)Math.Min(bufferSize, remaining);

						if (!ReadFully(file1, buffer1, readSize, token)
						 || !ReadFully(file2, buffer2, readSize, token))
							return false;

						if (!BuffersAreEqual(readSize))
							return false;

						remaining -= readSize;
					}

					// If we get here then we've compared every byte and the files are identical.
					return true;
				}
			}
			catch
			{
				return false;
			}
		}

		public static bool ReadFully(Stream file, byte[] buffer, int length)
			=> ReadFully(file, buffer, length, CancellationToken.None);

		public static bool ReadFully(Stream file, byte[] buffer, int length, CancellationToken token)
		{
			long lengthAtStart = file.Length;

			int offset = 0;

			while (offset < length)
			{
				if (token.IsCancellationRequested)
					return false;

				int numRead = file.Read(buffer, offset, length - offset);

				if (numRead <= 0)
					return false;

				// If the file's size changes as we're watching it, bail.
				if (file.Length != lengthAtStart)
					return false;

				offset += numRead;
			}

			return true;
		}
	}
}
