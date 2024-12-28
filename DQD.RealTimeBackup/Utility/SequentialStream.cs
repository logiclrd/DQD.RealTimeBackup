using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace DQD.RealTimeBackup.Utility
{
	public class SequentialStream : Stream
	{
		Stream _wrapped;
		long _offset;
		long _apparentOffset;
		Exception? _exception;

		public SequentialStream(Stream toWrap)
		{
			_wrapped = toWrap;
		}

		public override bool CanRead => true;
		public override bool CanWrite => true;
		public override bool CanSeek => true; // It is a lie, but we will pretend when writing.

		public override long Length => _wrapped.Length;

		public override void SetLength(long length)
		{
		}

		public void SetException(Exception exception)
		{
			_exception = exception;
		}

		void CheckIfFaulted()
		{
			if (_exception != null)
				ExceptionDispatchInfo.Throw(_exception);
		}

		public override long Position
		{
			get => _apparentOffset;
			set => _apparentOffset = value;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			CheckIfFaulted();

			switch (origin)
			{
				case SeekOrigin.Begin: Position = offset; break;
				case SeekOrigin.Current: Position += offset; break;
				case SeekOrigin.End: throw new InvalidOperationException("Cannot seek relative to the end of a SequentialStream");
			}

			return Position;
		}

		public override void Flush() { CheckIfFaulted(); _wrapped.Flush(); }
		public override Task FlushAsync(CancellationToken cancellationToken) { CheckIfFaulted(); return _wrapped.FlushAsync(cancellationToken); }

		protected override void Dispose(bool disposing)
		{
			_wrapped.Dispose();
		}

		public override ValueTask DisposeAsync()
		{
			return _wrapped.DisposeAsync();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			CheckIfFaulted();

			int numRead = _wrapped.Read(buffer, offset, count);

			_offset += numRead;
			_apparentOffset += numRead;

			return numRead;
		}

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			CheckIfFaulted();

			if (_apparentOffset != _offset)
				throw new InvalidOperationException("Cannot read when the SequentialStream's apparent offset is not synchronized with the underlying stream's actual offset.");

			int numRead = await _wrapped.ReadAsync(buffer, offset, count, cancellationToken);

			_offset += numRead;
			_apparentOffset += numRead;

			return numRead;
		}

		public override int ReadByte()
		{
			CheckIfFaulted();

			if (_apparentOffset != _offset)
				throw new InvalidOperationException("Cannot read when the SequentialStream's apparent offset is not synchronized with the underlying stream's actual offset.");

			int result = _wrapped.ReadByte();

			if (result >= 0)
			{
				_offset++;
				_apparentOffset++;
			}

			return result;
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			CheckIfFaulted();

			if (_apparentOffset > _offset)
				throw new InvalidOperationException("Cannot write when the apparent offset is past the underlying stream's actual offset.");

			long ghostBytes = _offset - _apparentOffset;

			if (ghostBytes > 0)
			{
				if (ghostBytes >= count)
				{
					_apparentOffset += count;
					return;
				}

				_apparentOffset += ghostBytes;
				offset += (int)ghostBytes;
				count -= (int)ghostBytes;
			}

			_wrapped.Write(buffer, offset, count);

			_offset += count;
			_apparentOffset += count;
		}

		public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			CheckIfFaulted();

			if (_apparentOffset > _offset)
				throw new InvalidOperationException("Cannot write when the apparent offset is past the underlying stream's actual offset.");

			long ghostBytes = _offset - _apparentOffset;

			if (ghostBytes > 0)
			{
				if (ghostBytes >= count)
				{
					_apparentOffset += count;
					return;
				}

				_apparentOffset += ghostBytes;
				offset += (int)ghostBytes;
				count -= (int)ghostBytes;
			}

			await _wrapped.WriteAsync(buffer, offset, count, cancellationToken);

			_offset += count;
			_apparentOffset += count;
		}

		public override void WriteByte(byte value)
		{
			CheckIfFaulted();

			if (_apparentOffset > _offset)
				throw new InvalidOperationException("Cannot write when the apparent offset is past the underlying stream's actual offset.");

			if (_apparentOffset == _offset)
			{
				_wrapped.WriteByte(value);
				_offset++;
			}

			_apparentOffset++;
		}
	}
}