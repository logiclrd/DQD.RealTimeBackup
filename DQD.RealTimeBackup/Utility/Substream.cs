using System;
using System.IO;

namespace DQD.RealTimeBackup.Utility
{
	public class Substream : Stream
	{
		Stream _underlying;
		long _offset;
		int _length;

		public Substream(Stream underlying, long offset, int length)
		{
			_underlying = underlying;
			_offset = offset;
			_length = length;

			_underlying.Position = _offset;
		}

		public override bool CanRead => _underlying.CanRead;
		public override bool CanWrite => _underlying.CanWrite;
		public override bool CanSeek => _underlying.CanSeek;

		public override long Position
		{
			get => _underlying.Position - _offset;
			set => _underlying.Position = _offset + value;
		}

		public override long Length => _length;

		public override void SetLength(long newValue)
		{
			_length = (int)newValue;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			if (origin == SeekOrigin.Begin)
				offset += _offset;

			return _underlying.Seek(offset, origin) - _offset;
		}

		public override int Read(byte[] buffer, int offset, int length)
		{
			if (Position + length > Length)
				length = (int)(Length - Position);

			return _underlying.Read(buffer, offset, length);
		}

		public override void Write(byte[] buffer, int offset, int length)
		{
			if (Position + length > Length)
				throw new InvalidOperationException("Cannot write past the end of a substream");

			_underlying.Write(buffer, offset, length);
		}

		public override void Flush() => _underlying.Flush();
	}
}
