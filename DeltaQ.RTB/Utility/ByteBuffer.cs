using System;
using System.Net.Sockets;
using System.Text;

namespace DeltaQ.RTB.Utility
{
	public class ByteBuffer
	{
		internal byte[]? _buffer;
		internal int _offset;
		internal int _length;

		public int Length => _length;
		public bool IsEmpty => (_length == 0);

		public ByteBuffer()
		{
		}

		public ByteBuffer(byte[] buffer)
		{
			_buffer = buffer;
			_offset = 0;
			_length = buffer.Length;
		}

		public ByteBuffer(byte[] buffer, int offset, int length)
		{
			_buffer = buffer;
			_offset = offset;
			_length = length;
		}

		public static ByteBuffer CopyOf(byte[] buffer)
			=> CopyOf(buffer, 0, buffer.Length);

		public static ByteBuffer CopyOf(byte[] buffer, int offset, int length)
		{
			byte[] copy = new byte[length];

			Array.Copy(buffer, offset, copy, 0, length);

			return new ByteBuffer(copy);
		}

		public static ByteBuffer DuplicateBuffer(byte[] buffer, int offset, int length)
		{
			byte[] copy = new byte[length];

			Array.Copy(buffer, offset, copy, offset, length);

			return new ByteBuffer(copy, offset, length);
		}

		public void Read(byte[] target, int offset, int count)
		{
			if (_buffer == null)
				throw new InvalidOperationException("Cannot read from a ByteBuffer with no underlying storage.");

			if (offset < 0)
				throw new ArgumentOutOfRangeException(nameof(offset));
			if ((count < 0) || (count > _length) || (count + offset > target.Length))
				throw new ArgumentOutOfRangeException(nameof(count));

			Array.Copy(_buffer, _offset, target, offset, count);

			Consume(count);
		}

		public byte ReadByte()
		{
			if (_buffer == null)
				throw new InvalidOperationException("Cannot read from a ByteBuffer with no underlying storage.");

			byte value = _buffer[_offset];

			Consume(1);

			return value;
		}

		public bool TryPeekInt32(out int value)
		{
			if (_buffer == null)
				throw new InvalidOperationException("Cannot read from a ByteBuffer with no underlying storage.");

			if (_length < 4)
			{
				value = default;
				return false;
			}
			else
			{
				value = BitConverter.ToInt32(_buffer, _offset);
				return true;
			}
		}

		public int ReadInt32()
		{
			if (_buffer == null)
				throw new InvalidOperationException("Cannot read from a ByteBuffer with no underlying storage.");
			if (_length < 4)
				throw new ArgumentOutOfRangeException();

			int value = BitConverter.ToInt32(_buffer, _offset);

			Consume(4);

			return value;
		}

		public long ReadInt64()
		{
			if (_buffer == null)
				throw new InvalidOperationException("Cannot read from a ByteBuffer with no underlying storage.");
			if (_length < 8)
				throw new ArgumentOutOfRangeException();

			long value = BitConverter.ToInt64(_buffer, _offset);

			Consume(8);

			return value;
		}

		public string ReadString()
		{
			if (_buffer == null)
				throw new InvalidOperationException("Cannot read from a ByteBuffer with no underlying storage.");

			int stringLength = ReadInt32();

			if (stringLength > _length)
				throw new Exception($"Attempting to a read a string of length {stringLength} but there are only {_length} bytes in the buffer");

			string str = Encoding.UTF8.GetString(_buffer, _offset, stringLength);

			Consume(stringLength);

			return str;
		}

		public void Consume(int count)
		{
			if ((count < 0) || (count > _length))
				throw new ArgumentOutOfRangeException(nameof(count));

			_offset += count;
			_length -= count;
		}

		public void Clear()
		{
			_offset = 0;
			_length = 0;
		}

		public void Release()
		{
			Clear();
			_buffer = null;
		}

		public void Append(byte[] bytes)
			=> Append(bytes, 0, bytes.Length);

		public void Append(byte[] bytes, int offset, int count)
		{
			if (_buffer == null)
				EnsureRemainingSpace(count);

			int remainingSpace = _buffer!.Length - _length - _offset;

			if (remainingSpace < count)
				EnsureRemainingSpace(count);

			Array.Copy(bytes, offset, _buffer, _offset + _length, count);

			_length += count;
		}

		public void AppendByte(byte b)
		{
			if ((_buffer == null) || (_offset + _length == _buffer.Length))
				EnsureRemainingSpace(50);

			_buffer![_offset + _length] = b;

			_length++;
		}

		public void AppendInt32(int value)
		{
			Append(BitConverter.GetBytes(value));
		}

		public void AppendInt64(long value)
		{
			Append(BitConverter.GetBytes(value));
		}

		public void AppendString(string value)
		{
			var utf8Bytes = Encoding.UTF8.GetBytes(value);

			AppendInt32(utf8Bytes.Length);
			Append(utf8Bytes);
		}

		public void Consolidate()
		{
			if (_buffer == null)
				throw new InvalidOperationException("Cannot consolidate a ByteBuffer with no underlying storage.");

			if (_offset > 100)
			{
				if (_buffer.Length > _length * 3)
					Shrink();
				else
				{
					Array.Copy(_buffer, _offset, _buffer, 0, _length);
					_offset = 0;
				}
			}
		}

		public void Shrink(int keepSpace = 50)
		{
			if (_buffer == null)
				throw new InvalidOperationException("Cannot shrink a ByteBuffer with no underlying storage.");

			int extraSpace = _buffer.Length - _length;

			if (extraSpace > keepSpace)
			{
				byte[] smallerBuffer = new byte[_length + keepSpace];

				Array.Copy(_buffer, _offset, smallerBuffer, 0, _length);

				_offset = 0;
				_buffer = smallerBuffer;
			}
		}

		public void EnsureRemainingSpace(int count)
		{
			if (_buffer == null)
			{
				_buffer = new byte[count];
				return;
			}

			int remainingSpace = _buffer.Length - _length - _offset;

			if (remainingSpace < count)
			{
				if (count < _buffer.Length / 2)
					count = _buffer.Length / 2;

				int extraSpace = _buffer.Length - _length;

				if (extraSpace >= count)
				{
					Array.Copy(_buffer, _offset, _buffer, 0, _length);
					_offset = 0;
				}
				else
				{
					byte[] largerBuffer = new byte[_length + count];

					Array.Copy(_buffer, _offset, largerBuffer, 0, _length);

					_offset = 0;
					_buffer = largerBuffer;
				}
			}
		}

		public byte this[int index]
		{
			get
			{
				if (_buffer == null)
					throw new InvalidOperationException("Cannot index a ByteBuffer with no underlying storage.");
				if (index >= _length)
					throw new IndexOutOfRangeException(nameof(index));

				return _buffer[_offset + index];
			}
			set
			{
				if ((index >= 0) && (index < _length))
					_buffer![_offset + index] = value;
				else if (index == _length)
					AppendByte(value);
				else
					throw new IndexOutOfRangeException(nameof(index));
			}
		}

		public int SendOnceToSocket(Socket destination)
		{
			if (_buffer == null)
				throw new InvalidOperationException("Cannot send a ByteBuffer with no underlying storage.");

			if (_length > 0)
			{
				int bytesSent = destination.Send(_buffer, _offset, _length, SocketFlags.None);

				_length -= bytesSent;

				if (_length > 0)
					_offset += bytesSent;
				else
					_offset = 0;

				return bytesSent;
			}

			return 0;
		}

		public void SendFullyToSocket(Socket destination)
		{
			while (_length > 0)
				SendOnceToSocket(destination);
		}

		public void SendLengthToSocket(Socket destination)
		{
			var buffer = BitConverter.GetBytes(_length);

			int offset = 0;
			int remaining = buffer.Length;

			while (remaining > 0)
			{
				int bytesSent = destination.Send(buffer, offset, remaining, SocketFlags.None);

				remaining -= bytesSent;
			}
		}

		public void ReceiveFromSocket(Socket source, int count)
		{
			EnsureRemainingSpace(count);

			while (count > 0)
			{
				int readSize = source.Receive(_buffer!, _offset + _length, count, SocketFlags.None);

				if (readSize <= 0)
					throw new Exception($"Receive error: requested {count} bytes, but the return value from Receive was {readSize}");

				_length += readSize;
				count -= readSize;
			}
		}
	}
}
