using System;
using System.IO;

namespace DQD.RealTimeBackup.Utility
{
	public class ReadProgressStream : Stream
	{
		Stream _underlying;
		Action<double>? _progressCallback;

		long _readOffset;
		double _expectedSize;

		public ReadProgressStream(Stream underlying, Action<double>? progressCallback)
		{
			_underlying = underlying;
			_progressCallback = progressCallback;

			_readOffset = 0;
			_expectedSize = _underlying.Length;
		}

		public ReadProgressStream(Stream underlying, long expectedSize, Action<double>? progressCallback)
		{
			_underlying = underlying;
			_progressCallback = progressCallback;

			_readOffset = 0;
			_expectedSize = expectedSize;
		}

		public double Progress => _readOffset / _expectedSize;

		public override bool CanRead => _underlying.CanRead;

		public override bool CanSeek => _underlying.CanSeek;

		public override bool CanWrite => _underlying.CanWrite;

		public override long Length => _underlying.Length;

		public override long Position
		{
			get => _underlying.Position;
			set => Seek(value, SeekOrigin.Begin);
		}

		public override void Flush()
		{
			_underlying.Flush();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int numRead = _underlying.Read(buffer, offset, count);

			_readOffset += numRead;

			_progressCallback?.Invoke(Progress);

			return numRead;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			_readOffset = _underlying.Seek(offset, origin);

			return _readOffset;
		}

		public override void SetLength(long value)
		{
			_underlying.SetLength(value);

			_expectedSize = value;
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			_underlying.Write(buffer, offset, count);
		}
	}
}
