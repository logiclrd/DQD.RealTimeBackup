using System;
using System.IO;
using System.Text;

namespace DQD.RealTimeBackup.Utility
{
	public class UTF8FromTextReaderStream : Stream
	{
		TextReader _underlying;
		char[] _incoming;
		byte[] _converted;
		int _convertedLength;
		int _convertedOffset;

		internal const int IncomingBufferSize = 2048;
		internal const int ConvertedBufferSize = IncomingBufferSize * 4; // max 4 bytes per UTF-8 character per the latest spec

		internal int ConvertedLength => _convertedLength;
		internal int ConvertedOffset => _convertedOffset;
		internal int ConvertedRemaining => _convertedLength - _convertedOffset;

		public UTF8FromTextReaderStream(TextReader underlying)
		{
			_underlying = underlying;
			_incoming = new char[IncomingBufferSize];
			_converted = new byte[ConvertedBufferSize];
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if ((offset < 0) || (offset > buffer.Length))
				throw new ArgumentOutOfRangeException(nameof(offset));

			if ((count < 0) || (offset + count > buffer.Length))
				throw new ArgumentOutOfRangeException(nameof(count));

			int remaining = _convertedLength - _convertedOffset;

			if (remaining <= 0)
			{
				ConvertSome();

				remaining = _convertedLength - _convertedOffset;

				if (remaining <= 0)
					return 0;
			}

			if (count > remaining)
			{
				Array.Copy(_converted, _convertedOffset, buffer, offset, remaining);
				_convertedOffset = _convertedLength;
				return remaining;
			}
			else
			{
				Array.Copy(_converted, _convertedOffset, buffer, offset, count);
				_convertedOffset += count;
				return count;
			}
		}

		void ConvertSome()
		{
			if (_convertedLength > _convertedOffset)
				throw new InvalidOperationException("ConvertSome called when the buffer isn't empty");

			int incomingLength = _underlying.Read(_incoming);

			if (incomingLength > 0)
				_convertedLength = Encoding.UTF8.GetBytes(_incoming, 0, incomingLength, _converted, 0);
			else
				_convertedLength = 0;

			_convertedOffset = 0;
		}

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override void Flush() { }
	}
}

