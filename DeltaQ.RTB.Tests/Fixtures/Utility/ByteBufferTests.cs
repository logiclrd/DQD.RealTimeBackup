using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Bogus;

using FluentAssertions;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Tests.Fixtures.Utility
{
	[TestFixture]
	public class ByteBufferTests
	{
		[Test]
		public void Read_should_throw_if_ByteBuffer_has_not_been_initialized()
		{
			// Arrange
			var sut = new ByteBuffer();

			var readBuffer = new byte[1];

			// Act
			Action action = () => sut.Read(readBuffer, 0, 1);

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void Read_should_throw_if_ByteBuffer_has_been_released()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			var readBuffer = new byte[1];

			sut.Release();

			// Act
			Action action = () => sut.Read(readBuffer, 0, 1);

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void Read_should_do_nothing_when_count_is_zero()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			byte[] buffer = Array.Empty<byte>();

			// Act
			var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

			sut.Read(buffer, 0, 0);

			var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

			// Assert
			afterBuffer.Should().BeSameAs(beforeBuffer);
			afterOffset.Should().Be(beforeOffset);
			afterLength.Should().Be(beforeLength);
		}

		[TestCase(0, 0, 1)]
		[TestCase(0, 0, -1)]
		[TestCase(0, -1, 0)]
		[TestCase(1, 0, 2)]
		[TestCase(10, 5, 10)]
		[TestCase(10, 5, -5)]
		[TestCase(10, -1, 1)]
		public void Read_should_throw_if_offset_and_count_are_out_of_range_of_target(int targetBufferLength, int testOffset, int testCount)
		{
			// Arrange
			var sut = new ByteBuffer(new byte[100]);

			var buffer = new byte[targetBufferLength];

			// Act
			Action action = () => sut.Read(buffer, testOffset, testCount);

			// Assert
			action.Should().Throw<ArgumentOutOfRangeException>();
		}

		[TestCase(0, 1)]
		[TestCase(1, 2)]
		[TestCase(10, 11)]
		[TestCase(10, 20)]
		public void Read_should_throw_if_count_is_out_of_range_of_source(int testBufferLength, int testCount)
		{
			// Arrange
			var faker = new Faker();

			var testBytes = faker.Random.Bytes(testBufferLength);

			var sut = new ByteBuffer(testBytes);

			var readBuffer = new byte[testCount];

			// Act
			var action = () => sut.Read(readBuffer, 0, testCount);

			// Assert
			action.Should().Throw<ArgumentOutOfRangeException>();
		}

		[TestCase(1, 1)]
		[TestCase(10, 1)]
		[TestCase(10, 5)]
		[TestCase(10, 10)]
		public void Read_should_extract_bytes_from_buffer(int testBufferLength, int testReadCount)
		{
			// Arrange
			var faker = new Faker();

			var testBytes = faker.Random.Bytes(testBufferLength);

			var sut = new ByteBuffer(testBytes);

			var readBuffer = new byte[testReadCount];

			// Act
			sut.Read(readBuffer, 0, testReadCount);

			// Assert
			readBuffer.Should().BeEquivalentTo(testBytes.Take(testReadCount), options => options.WithStrictOrdering());
		}

		[TestCase(1, 0, 1)]
		[TestCase(10, 0, 1)]
		[TestCase(10, 0, 5)]
		[TestCase(10, 2, 5)]
		[TestCase(10, 5, 5)]
		[TestCase(10, 0, 10)]
		public void Read_should_advance_offset(int testBufferLength, int startingOffset, int testReadCount)
		{
			// Arrange
			var faker = new Faker();

			var testBytes = faker.Random.Bytes(testBufferLength);

			var sut = new ByteBuffer(testBytes, startingOffset, testBufferLength - startingOffset);

			var readBuffer = new byte[testReadCount];

			// Act
			int beforeOffset = sut._offset;

			sut.Read(readBuffer, 0, testReadCount);

			int afterOffset = sut._offset;

			// Assert
			beforeOffset.Should().Be(startingOffset);
			afterOffset.Should().Be(startingOffset + testReadCount);
		}

		[Test]
		public void ReadByte_should_throw_if_ByteBuffer_has_not_been_initialized()
		{
			// Arrange
			var sut = new ByteBuffer();

			// Act
			Action action = () => sut.ReadByte();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void ReadByte_should_throw_if_ByteBuffer_has_been_released()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			sut.Release();

			// Act
			Action action = () => sut.ReadByte();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void ReadByte_should_read_byte_from_buffer()
		{
			// Arrange
			var faker = new Faker();

			var expectedBytes = faker.Random.Bytes(200);

			var sut = new ByteBuffer(expectedBytes.ToArray());

			var actualBytes = new List<byte>();

			// Act
			for (int i=0; i < expectedBytes.Length; i++)
				actualBytes.Add(sut.ReadByte());

			// Assert
			actualBytes.Should().BeEquivalentTo(expectedBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void TryPeekInt32_should_throw_if_ByteBuffer_has_not_been_initialized()
		{
			// Arrange
			var sut = new ByteBuffer();

			// Act
			Action action = () => sut.TryPeekInt32(out var _);

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void TryPeekInt32_should_throw_if_ByteBuffer_has_been_released()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			sut.Release();

			// Act
			Action action = () => sut.TryPeekInt32(out var _);

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(2)]
		[TestCase(3)]
		public void TryPeekInt32_should_return_false_when_there_are_insufficient_bytes(int byteCount)
		{
			// Arrange
			var sut = new ByteBuffer(new byte[byteCount]);

			// Act
			var result = sut.TryPeekInt32(out var _);

			// Assert
			result.Should().BeFalse();
		}

		[TestCase(4)]
		[TestCase(40)]
		public void TryPeekInt32_should_return_integer_value_without_consuming(int bufferSize)
		{
			// Arrange
			var faker = new Faker();

			int int32Value = faker.Random.Int();

			var sut = new ByteBuffer(new byte[bufferSize], 0, 0);

			sut.AppendInt32(int32Value);

			while (sut.Length < bufferSize)
				sut.AppendByte(faker.Random.Byte());

			// Act
			var result = sut.TryPeekInt32(out var peekedValue);

			// Assert
			result.Should().BeTrue();
			peekedValue.Should().Be(int32Value);
		}

		[Test]
		public void ReadInt32_should_throw_if_ByteBuffer_has_not_been_initialized()
		{
			// Arrange
			var sut = new ByteBuffer();

			// Act
			Action action = () => sut.ReadInt32();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void ReadInt32_should_throw_if_ByteBuffer_has_been_released()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			sut.Release();

			// Act
			Action action = () => sut.ReadInt32();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void ReadInt32_should_convert_bytes_to_int()
		{
			// Arrange
			var faker = new Faker();

			var expectedNumbers = new List<int>();
			var numberBytes = new List<byte>();

			for (int i=0; i < 200; i++)
			{
				expectedNumbers.Add(faker.Random.Int());
				numberBytes.AddRange(BitConverter.GetBytes(expectedNumbers[i]));
			}

			var sut = new ByteBuffer(numberBytes.ToArray());

			var actualNumbers = new List<int>();

			// Act
			for (int i=0; i < expectedNumbers.Count; i++)
				actualNumbers.Add(sut.ReadInt32());

			// Assert
			actualNumbers.Should().BeEquivalentTo(expectedNumbers, options => options.WithStrictOrdering());
		}

		[Test]
		public void ReadInt64_should_throw_if_ByteBuffer_has_not_been_initialized()
		{
			// Arrange
			var sut = new ByteBuffer();

			// Act
			Action action = () => sut.ReadInt64();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void ReadInt64_should_throw_if_ByteBuffer_has_been_released()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			sut.Release();

			// Act
			Action action = () => sut.ReadInt64();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void ReadInt64_should_convert_bytes_to_long()
		{
			// Arrange
			var faker = new Faker();

			var expectedNumbers = new List<long>();
			var numberBytes = new List<byte>();

			for (int i=0; i < 200; i++)
			{
				expectedNumbers.Add(faker.Random.Long());
				numberBytes.AddRange(BitConverter.GetBytes(expectedNumbers[i]));
			}

			var sut = new ByteBuffer(numberBytes.ToArray());

			var actualNumbers = new List<long>();

			// Act
			for (int i=0; i < expectedNumbers.Count; i++)
				actualNumbers.Add(sut.ReadInt64());

			// Assert
			actualNumbers.Should().BeEquivalentTo(expectedNumbers, options => options.WithStrictOrdering());
		}

		[Test]
		public void ReadString_should_throw_if_ByteBuffer_has_not_been_initialized()
		{
			// Arrange
			var sut = new ByteBuffer();

			// Act
			Action action = () => sut.ReadString();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void ReadString_should_throw_if_ByteBuffer_has_been_released()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			sut.Release();

			// Act
			Action action = () => sut.ReadString();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void ReadString_should_pull_length_prefixed_string_from_buffer()
		{
			// Arrange
			var faker = new Faker();

			var testString = faker.Random.String(1000);

			var testBytes = Encoding.UTF8.GetBytes(testString);

			// Faker might give us a string with UTF errors in it.
			testString = Encoding.UTF8.GetString(testBytes);
			testBytes = Encoding.UTF8.GetBytes(testString);

			var lengthBytes = BitConverter.GetBytes(testBytes.Length);

			var extraBytes = faker.Random.Bytes(10);

			var buffer = lengthBytes.Concat(testBytes).Concat(extraBytes).ToArray();

			var sut = new ByteBuffer(buffer);

			// Act
			var beforeOffset = sut._offset;

			var actualString = sut.ReadString();

			var afterOffset = sut._offset;

			// Assert
			actualString.Should().Be(testString);

			afterOffset.Should().Be(beforeOffset += lengthBytes.Length + testBytes.Length);

			sut.Length.Should().Be(buffer.Length - lengthBytes.Length - testBytes.Length);
		}

		[TestCase(-1)]
		[TestCase(11)]
		public void Consume_should_throw_if_count_is_out_of_range(int testCount)
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10], 1, 9);

			// Act
			Action action = () => sut.Consume(testCount);

			// Assert
			action.Should().Throw<ArgumentOutOfRangeException>();
		}

		[Test]
		public void Consume_should_advance_pointer_appropriately()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10], 1, 9);

			int testCount = 3;

			// Act
			var (beforeOffset, beforeLength) = (sut._offset, sut._length);

			sut.Consume(testCount);

			var (afterOffset, afterLength) = (sut._offset, sut._length);

			// Assert
			afterOffset.Should().Be(beforeOffset + testCount);
			afterLength.Should().Be(beforeLength - testCount);
		}

		[Test]
		public void Clear_should_result_in_zero_Length_and_IsEmpty_true()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10], 1, 9);

			// Act
			var (beforeLength, beforeIsEmpty) = (sut.Length, sut.IsEmpty);

			sut.Clear();

			var (afterLength, afterIsEmpty) = (sut.Length, sut.IsEmpty);

			// Assert
			beforeLength.Should().NotBe(0);
			beforeIsEmpty.Should().BeFalse();

			afterLength.Should().Be(0);
			afterIsEmpty.Should().BeTrue();
		}

		[Test]
		public void Append_should_allocate_buffer_if_there_is_none()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			var testBytes = faker.Random.Bytes(10);

			// Act
			sut.Append(testBytes, 0, testBytes.Length);

			// Assert
			sut._buffer.Should().NotBeNull();
			sut._buffer!.Skip(sut._offset).Take(sut._length).Should().BeEquivalentTo(testBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void Append_should_reallocate_buffer_if_there_is_not_enough_space()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			sut.EnsureRemainingSpace(10);

			var initialBytes = faker.Random.Bytes(5);
			var bytesToAppend = faker.Random.Bytes(10);

			sut.Append(initialBytes);

			// Act
			var bufferBefore = sut._buffer;

			sut.Append(bytesToAppend, 0, bytesToAppend.Length);

			var bufferAfter = sut._buffer;

			// Assert
			bufferAfter.Should().NotBeSameAs(bufferBefore);

			sut.Length.Should().Be(initialBytes.Length + bytesToAppend.Length);

			sut._buffer!.Skip(sut._offset).Take(sut._length).Should().BeEquivalentTo(initialBytes.Concat(bytesToAppend), options => options.WithStrictOrdering());
		}

		[Test]
		public void Append_should_append_bytes()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			sut.EnsureRemainingSpace(10);

			var initialBytes = faker.Random.Bytes(2);
			var bytesToAppend = faker.Random.Bytes(5);

			sut.Append(initialBytes);

			// Act
			var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

			sut.Append(bytesToAppend, 0, bytesToAppend.Length);

			var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

			// Assert
			afterBuffer.Should().BeSameAs(beforeBuffer);
			afterOffset.Should().Be(beforeOffset);
			afterLength.Should().Be(beforeLength + bytesToAppend.Length);

			sut._buffer!.Skip(sut._offset + initialBytes.Length).Take(bytesToAppend.Length)
				.Should().BeEquivalentTo(bytesToAppend, options => options.WithStrictOrdering());
		}

		[Test]
		public void AppendByte_should_allocate_buffer_if_there_is_none()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			var testByte = faker.Random.Byte();

			// Act
			sut.AppendByte(testByte);

			// Assert
			sut._buffer.Should().NotBeNull();
			sut._length.Should().Be(1);
			sut._buffer![sut._offset].Should().Be(testByte);
		}

		[Test]
		public void AppendByte_should_reallocate_buffer_if_there_is_not_enough_space()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			sut.EnsureRemainingSpace(10);

			var initialBytes = faker.Random.Bytes(10);
			var byteToAppend = faker.Random.Byte();

			sut.Append(initialBytes);

			// Act
			var bufferBefore = sut._buffer;

			sut.AppendByte(byteToAppend);

			var bufferAfter = sut._buffer;

			// Assert
			bufferAfter.Should().NotBeSameAs(bufferBefore);

			sut.Length.Should().Be(initialBytes.Length + 1);

			sut._buffer![sut._offset + initialBytes.Length].Should().Be(byteToAppend);
		}

		[Test]
		public void AppendByte_should_append_bytes()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			sut.EnsureRemainingSpace(10);

			var initialBytes = faker.Random.Bytes(2);
			var bytesToAppend = faker.Random.Bytes(5);

			sut.Append(initialBytes);

			// Act
			var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

			foreach (var byteToAppend in bytesToAppend)
				sut.AppendByte(byteToAppend);

			var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

			// Assert
			afterBuffer.Should().BeSameAs(beforeBuffer);
			afterOffset.Should().Be(beforeOffset);
			afterLength.Should().Be(beforeLength + bytesToAppend.Length);

			sut._buffer!.Skip(sut._offset + initialBytes.Length).Take(bytesToAppend.Length)
				.Should().BeEquivalentTo(bytesToAppend, options => options.WithStrictOrdering());
		}

		[Test]
		public void AppendInt32_should_append_integer_values()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			sut.EnsureRemainingSpace(50);

			var initialBytes = faker.Random.Bytes(2);
			var intsToAppend = new List<int>();
			var intsToAppendBytes = new List<byte>();

			for (int i=0; i < 5; i++)
			{
				intsToAppend.Add(faker.Random.Int());
				intsToAppendBytes.AddRange(BitConverter.GetBytes(intsToAppend[i]));
			}

			sut.Append(initialBytes);

			// Act
			var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

			foreach (var intToAppend in intsToAppend)
				sut.AppendInt32(intToAppend);

			var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

			// Assert
			afterBuffer.Should().BeSameAs(beforeBuffer);
			afterOffset.Should().Be(beforeOffset);
			afterLength.Should().Be(beforeLength + intsToAppend.Count * 4);

			sut._buffer!.Skip(sut._offset + initialBytes.Length).Take(intsToAppend.Count * 4)
				.Should().BeEquivalentTo(intsToAppendBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void AppendInt64_should_append_integer_values()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			sut.EnsureRemainingSpace(50);

			var initialBytes = faker.Random.Bytes(2);
			var longsToAppend = new List<long>();
			var longsToAppendBytes = new List<byte>();

			for (int i=0; i < 5; i++)
			{
				longsToAppend.Add(faker.Random.Long());
				longsToAppendBytes.AddRange(BitConverter.GetBytes(longsToAppend[i]));
			}

			sut.Append(initialBytes);

			// Act
			var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

			foreach (var longToAppend in longsToAppend)
				sut.AppendInt64(longToAppend);

			var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

			// Assert
			afterBuffer.Should().BeSameAs(beforeBuffer);
			afterOffset.Should().Be(beforeOffset);
			afterLength.Should().Be(beforeLength + longsToAppend.Count * 8);

			sut._buffer!.Skip(sut._offset + initialBytes.Length).Take(longsToAppend.Count * 8)
				.Should().BeEquivalentTo(longsToAppendBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void AppendString_should_append_strings()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			var initialBytes = faker.Random.Bytes(2);
			var stringsToAppend = new List<string>();
			var stringsToAppendBytes = new List<byte>();

			for (int i=0; i < 5; i++)
			{
				stringsToAppend.Add(faker.Random.String(200));

				byte[] stringBytes = Encoding.UTF8.GetBytes(stringsToAppend[i]);

				stringsToAppendBytes.AddRange(BitConverter.GetBytes(stringBytes.Length));
				stringsToAppendBytes.AddRange(stringBytes);
			}

			sut.EnsureRemainingSpace(stringsToAppendBytes.Count + 50);

			sut.Append(initialBytes);

			// Act
			var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

			foreach (var stringToAppend in stringsToAppend)
				sut.AppendString(stringToAppend);

			var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

			// Assert
			afterBuffer.Should().BeSameAs(beforeBuffer);
			afterOffset.Should().Be(beforeOffset);
			afterLength.Should().Be(beforeLength + stringsToAppendBytes.Count);

			sut._buffer!.Skip(sut._offset + initialBytes.Length).Take(stringsToAppendBytes.Count)
				.Should().BeEquivalentTo(stringsToAppendBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void Consolidate_should_throw_if_ByteBuffer_has_not_been_initialized()
		{
			// Arrange
			var sut = new ByteBuffer();

			// Act
			Action action = () => sut.Consolidate();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void Consolidate_should_throw_if_ByteBuffer_has_been_released()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			sut.Release();

			// Act
			Action action = () => sut.Consolidate();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(10)]
		[TestCase(99)]
		[TestCase(100)]
		public void Consolidate_should_do_nothing_if_offset_is_close_to_start(int testOffset)
		{
			// Arrange
			var faker = new Faker();

			var buffer = faker.Random.Bytes(200);

			var sut = new ByteBuffer(buffer, testOffset, 100);

			var expectedBytes = buffer.ToArray();

			// Act
			sut.Consolidate();

			// Assert
			sut._offset.Should().Be(testOffset);

			sut._buffer.Should().BeSameAs(buffer);

			sut._buffer.Should().BeEquivalentTo(expectedBytes, options => options.WithStrictOrdering());
		}

		[TestCase(101)]
		[TestCase(150)]
		[TestCase(200)]
		public void Consolidate_should_move_bytes_to_start_of_buffer(int testOffset)
		{
			// Arrange
			var faker = new Faker();

			var buffer = faker.Random.Bytes(250);

			var sut = new ByteBuffer(buffer, testOffset, buffer.Length - testOffset);

			var expectedBytes = buffer.Skip(testOffset).Take(sut.Length).ToArray();

			// Act
			sut.Consolidate();

			// Assert
			sut._offset.Should().Be(0);
			sut.Length.Should().Be(expectedBytes.Length);

			sut._buffer!.Take(expectedBytes.Length).Should().BeEquivalentTo(expectedBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void Consolidate_should_shrink_buffer_if_unused_space_is_above_threshold()
		{
			// Arrange
			var faker = new Faker();

			var buffer = faker.Random.Bytes(250);

			var sut = new ByteBuffer(buffer, 200, 50);

			var expectedBytes = buffer.Skip(sut._offset).Take(sut._length).ToArray();

			// Act
			sut.Consolidate();

			// Assert
			sut._offset.Should().Be(0);
			sut.Length.Should().Be(expectedBytes.Length);

			sut._buffer.Should().NotBeSameAs(buffer);
			sut._buffer!.Length.Should().BeLessThan(buffer.Length);

			sut._buffer!.Take(expectedBytes.Length).Should().BeEquivalentTo(expectedBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void Shrink_should_throw_if_ByteBuffer_has_not_been_initialized()
		{
			// Arrange
			var sut = new ByteBuffer();

			// Act
			Action action = () => sut.Shrink();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void Shrink_should_throw_if_ByteBuffer_has_been_released()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			sut.Release();

			// Act
			Action action = () => sut.Shrink();

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void Shrink_should_do_nothing_if_remaining_space_is_not_greater_than_requested_space_to_keep()
		{
			// Arrange
			var faker = new Faker();

			var buffer = faker.Random.Bytes(50);

			var sut = new ByteBuffer(buffer, 0, 10);

			var expectedBytes = buffer.ToArray();

			// Act
			var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

			sut.Shrink(keepSpace: 50);

			var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

			// Assert
			afterBuffer.Should().BeSameAs(beforeBuffer);
			afterOffset.Should().Be(beforeOffset);
			afterLength.Should().Be(beforeLength);
			sut._buffer!.Skip(sut._offset).Take(sut._length).Should().BeEquivalentTo(expectedBytes.Take(afterLength), options => options.WithStrictOrdering());
		}

		[Test]
		public void Shrink_should_reallocate_buffer_if_unused_space_is_above_threshold()
		{
			// Arrange
			var faker = new Faker();

			var buffer = faker.Random.Bytes(250);

			var sut = new ByteBuffer(buffer, 200, 50);

			var expectedBytes = buffer.Skip(sut._offset).Take(sut._length).ToArray();

			// Act
			sut.Shrink(keepSpace: 50);

			// Assert
			sut._offset.Should().Be(0);
			sut.Length.Should().Be(expectedBytes.Length);

			sut._buffer.Should().NotBeSameAs(buffer);
			sut._buffer!.Length.Should().BeLessThan(buffer.Length);

			sut._buffer!.Take(expectedBytes.Length).Should().BeEquivalentTo(expectedBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void EnsureRemainingSpace_should_initialize_buffer_on_uninitialized_instance()
		{
			// Arrange
			var sut = new ByteBuffer();

			// Act
			sut.EnsureRemainingSpace(10);

			// Assert
			sut._buffer.Should().NotBeNull();
			sut._buffer!.Length.Should().BeGreaterThanOrEqualTo(10);
		}

		[Test]
		public void EnsureRemainingSpace_should_initialize_buffer_on_released_instance()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[20]);

			sut.Release();

			// Act
			sut.EnsureRemainingSpace(10);

			// Assert
			sut._buffer.Should().NotBeNull();
			sut._buffer!.Length.Should().BeGreaterThanOrEqualTo(10);
		}

		[Test]
		public void EnsureRemainingSpace_should_do_nothing_if_unused_space_is_already_adequate()
		{
			// Arrange
			var faker = new Faker();

			var buffer = faker.Random.Bytes(50);

			var sut = new ByteBuffer(buffer, 0, 10);

			var expectedBytes = buffer.ToArray();

			// Act
			var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

			sut.EnsureRemainingSpace(30);

			var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

			// Assert
			afterBuffer.Should().BeSameAs(beforeBuffer);
			afterOffset.Should().Be(beforeOffset);
			afterLength.Should().Be(beforeLength);
			sut._buffer!.Skip(sut._offset).Take(sut._length)
				.Should().BeEquivalentTo(expectedBytes.Take(sut._length), options => options.WithStrictOrdering());
		}

		[Test]
		public void EnsureRemainingSpace_should_consolidate_buffer_if_there_is_enough_unused_space_but_not_enough_at_end()
		{
			// Arrange
			var faker = new Faker();

			var testBytes = faker.Random.Bytes(50);

			var sut = new ByteBuffer(testBytes, 30, 10);

			var expectedBytes = testBytes.Skip(30).Take(10).ToArray();

			// Act
			var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

			sut.EnsureRemainingSpace(30);

			var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

			// Assert
			afterBuffer.Should().BeSameAs(beforeBuffer);
			afterOffset.Should().Be(0);
			afterLength.Should().Be(beforeLength);

			var availableBytes = sut._buffer!.Length - sut._offset - sut._length;

			availableBytes.Should().BeGreaterThanOrEqualTo(30);

			sut._buffer!.Skip(sut._offset).Take(sut._length).Should().BeEquivalentTo(expectedBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void EnsureRemainingSpace_should_reallocate_buffer_if_there_is_not_enough_space()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			sut.EnsureRemainingSpace(10);

			var initialBytes = faker.Random.Bytes(5);

			sut.Append(initialBytes);

			// Act
			var bufferBefore = sut._buffer;

			sut.EnsureRemainingSpace(10);

			var bufferAfter = sut._buffer;

			// Assert
			bufferAfter.Should().NotBeSameAs(bufferBefore);

			sut.Length.Should().Be(initialBytes.Length);

			sut._buffer!.Skip(sut._offset).Take(sut._length).Should().BeEquivalentTo(initialBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void Get_indexer_should_throw_if_ByteBuffer_has_not_been_initialized()
		{
			// Arrange
			var sut = new ByteBuffer();

			byte readByte = default;

			// Act
			Action action = () => readByte = sut[0];

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void Get_indexer_should_throw_if_ByteBuffer_has_been_released()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			var readBuffer = new byte[1];

			sut.Release();

			byte readByte = default;

			// Act
			Action action = () => readByte = sut[0];

			// Assert
			action.Should().Throw<InvalidOperationException>();
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(10)]
		public void Get_indexer_should_throw_if_index_is_not_less_than_length(int howMuchLargerThanLength)
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			byte readByte = default;

			// Act
			Action action = () => readByte = sut[sut.Length + howMuchLargerThanLength];

			// Assert
			action.Should().Throw<IndexOutOfRangeException>();
		}

		[Test]
		public void Get_indexer_should_throw_if_index_is_negative()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			byte readByte = default;

			// Act
			Action action = () => readByte = sut[-1];

			// Assert
			action.Should().Throw<IndexOutOfRangeException>();
		}

		[Test]
		public void Get_indexer_should_read_bytes_from_buffer()
		{
			// Arrange
			var faker = new Faker();

			var testBytes = faker.Random.Bytes(20);

			var sut = new ByteBuffer(testBytes, 5, 10);

			var bytesRead = new List<byte>();

			// Act
			for (int i=0; i < sut.Length; i++)
				bytesRead.Add(sut[i]);

			// Assert
			bytesRead.Should().BeEquivalentTo(testBytes.Skip(sut._offset).Take(sut._length), options => options.WithStrictOrdering());
		}

		[Test]
		public void Set_indexer_should_write_byte_values_to_acceptable_indices()
		{
			// Arrange
			var faker = new Faker();

			var testBytes = faker.Random.Bytes(20);

			var sut = new ByteBuffer(testBytes, 5, 10);

			var newBytes = faker.Random.Bytes(sut.Length);

			// Act
			for (int i=0; i < sut.Length; i++)
				sut[i] = newBytes[i];

			// Assert
			sut._buffer!.Skip(sut._offset).Take(sut._length).Should().BeEquivalentTo(newBytes, options => options.WithStrictOrdering());
		}

		[Test]
		public void Set_indexer_should_initialize_buffer_on_uninitialized_instance()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			var testByte = faker.Random.Byte();

			// Act
			sut[0] = testByte;

			// Assert
			sut._buffer.Should().NotBeNull();
			sut._buffer![0].Should().Be(testByte);
			sut.Length.Should().Be(1);
		}

		[Test]
		public void Set_indexer_should_initialize_buffer_on_released_instance()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer(new byte[10]);

			sut.Release();

			var testByte = faker.Random.Byte();

			// Act
			sut[0] = testByte;

			// Assert
			sut._buffer.Should().NotBeNull();
			sut._buffer![0].Should().Be(testByte);
			sut.Length.Should().Be(1);
		}

		[Test]
		public void Set_indexer_should_append_bytes_when_index_equals_length()
		{
			// Arrange
			var faker = new Faker();

			var sut = new ByteBuffer();

			sut.EnsureRemainingSpace(10);

			var initialBytes = faker.Random.Bytes(2);
			var bytesToAppend = faker.Random.Bytes(5);

			sut.Append(initialBytes);

			// Act
			var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

			foreach (var byteToAppend in bytesToAppend)
				sut[sut.Length] = byteToAppend;

			var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

			// Assert
			afterBuffer.Should().BeSameAs(beforeBuffer);
			afterOffset.Should().Be(beforeOffset);
			afterLength.Should().Be(beforeLength + bytesToAppend.Length);

			sut._buffer!.Skip(sut._offset + initialBytes.Length).Take(bytesToAppend.Length)
				.Should().BeEquivalentTo(bytesToAppend, options => options.WithStrictOrdering());
		}

		[TestCase(-1)]
		[TestCase(10)]
		public void Set_accessor_should_throw_if_index_is_out_of_range(int testIndex)
		{
			// Arrange
			var sut = new ByteBuffer(new byte[5]);

			// Act
			Action action = () => sut[testIndex] = 1;

			// Assert
			action.Should().Throw<IndexOutOfRangeException>();
		}

		[Test]
		public void SendOnceToSocket_should_throw_if_ByteBuffer_has_not_been_initialized()
		{
			// Arrange
			var sut = new ByteBuffer();

			using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
			{
				// Act
				Action action = () => sut.SendOnceToSocket(socket);

				// Assert
				action.Should().Throw<InvalidOperationException>();
			}
		}

		[Test]
		public void SendOnceToSocket_should_throw_if_ByteBuffer_has_been_released()
		{
			// Arrange
			var sut = new ByteBuffer(new byte[10]);

			sut.Release();

			using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
			{
				// Act
				Action action = () => sut.SendOnceToSocket(socket);

				// Assert
				action.Should().Throw<InvalidOperationException>();
			}
		}

		[Test]
		public void SendOnceToSocket_should_deliver_bytes_to_socket()
		{
			// Arrange
			Socket? listener = null;
			Socket? sender = null;
			Socket? receiver = null;

			try
			{
				listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

				listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
				listener.Listen();

				var localEndPoint = listener.LocalEndPoint;

				if (localEndPoint == null)
					throw new Exception("Sanity failure");

				var acceptConnectionTask = Task.Run(
					() =>
					{
						receiver = listener.Accept();
						receiver.Blocking = false;
					});

				sender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				sender.Connect(localEndPoint);

				acceptConnectionTask.Wait();

				if (receiver == null)
					throw new Exception("Sanity failure");

				var faker = new Faker();

				var testBytes = faker.Random.Bytes(10000);

				int testOffset = faker.Random.Number(10, 30);

				var sut = new ByteBuffer(testBytes, testOffset, testBytes.Length - testOffset);

				var receiveBuffer = new byte[testBytes.Length];

				// Act
				var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

				sut.SendOnceToSocket(sender);

				var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

				// Assert
				int bytesReceived = 0;

				try
				{
					while (true)
					{
						int receiveLength = receiver.Receive(receiveBuffer, bytesReceived, receiveBuffer.Length - bytesReceived, SocketFlags.None);

						if (receiveLength <= 0)
							break;

						bytesReceived += receiveLength;
					}
				}
				catch (SocketException) {}

				Console.WriteLine("Bytes sent in a single send(): {0}", bytesReceived);

				afterBuffer.Should().BeSameAs(beforeBuffer);

				if (afterLength == 0)
					afterOffset.Should().Be(0);
				else
				{
					afterOffset.Should().Be(beforeOffset + bytesReceived);
					afterLength.Should().Be(beforeLength - bytesReceived);
				}

				receiveBuffer.Take(bytesReceived)
					.Should().BeEquivalentTo(testBytes.Skip(beforeOffset).Take(bytesReceived), options => options.WithStrictOrdering());
			}
			finally
			{
				listener?.Dispose();
				sender?.Dispose();
				receiver?.Dispose();
			}
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(10)]
		[TestCase(10000)]
		public void SendLengthToSocket_should_deliver_length_to_socket(int testLength)
		{
			// Arrange
			Socket? listener = null;
			Socket? sender = null;
			Socket? receiver = null;

			try
			{
				listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

				listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
				listener.Listen();

				var localEndPoint = listener.LocalEndPoint;

				if (localEndPoint == null)
					throw new Exception("Sanity failure");

				var acceptConnectionTask = Task.Run(
					() =>
					{
						receiver = listener.Accept();
						receiver.Blocking = false;
					});

				sender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				sender.Connect(localEndPoint);

				acceptConnectionTask.Wait();

				if (receiver == null)
					throw new Exception("Sanity failure");

				var faker = new Faker();

				var sut = new ByteBuffer(new byte[testLength]);

				// Act
				var (beforeBuffer, beforeOffset, beforeLength) = (sut._buffer, sut._offset, sut._length);

				sut.SendLengthToSocket(sender);

				var (afterBuffer, afterOffset, afterLength) = (sut._buffer, sut._offset, sut._length);

				// Assert
				int bytesReceived = 0;
				byte[] receiveBuffer = new byte[1024];

				try
				{
					while (true)
					{
						int receiveLength = receiver.Receive(receiveBuffer, bytesReceived, receiveBuffer.Length - bytesReceived, SocketFlags.None);

						if (receiveLength <= 0)
							break;

						bytesReceived += receiveLength;
					}
				}
				catch (SocketException) {}

				afterBuffer.Should().BeSameAs(beforeBuffer);
				afterOffset.Should().Be(beforeOffset);
				afterLength.Should().Be(beforeLength);

				bytesReceived.Should().Be(4);

				int receivedLength = BitConverter.ToInt32(receiveBuffer);

				receivedLength.Should().Be(sut.Length);
			}
			finally
			{
				listener?.Dispose();
				sender?.Dispose();
				receiver?.Dispose();
			}
		}

		[TestCase(1, 1)]
		[TestCase(1, 10)]
		[TestCase(10, 1)]
		[TestCase(1024, 50)]
		[TestCase(1048576, 2)]
		public void ReceiveFromSocket_should_place_received_bytes_into_buffer_properly(int bytesPerTransfer, int transferCount)
		{
			// Arrange
			Socket? listener = null;
			Socket? sender = null;
			Socket? receiver = null;

			try
			{
				listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

				listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
				listener.Listen();

				var localEndPoint = listener.LocalEndPoint;

				if (localEndPoint == null)
					throw new Exception("Sanity failure");

				var acceptConnectionTask = Task.Run(
					() =>
					{
						receiver = listener.Accept();
					});

				sender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				sender.Connect(localEndPoint);

				acceptConnectionTask.Wait();

				if (receiver == null)
					throw new Exception("Sanity failure");

				var faker = new Faker();

				int totalBytes = bytesPerTransfer * transferCount;

				var testBytes = faker.Random.Bytes(totalBytes);

				var sut = new ByteBuffer();

				// Act
				var sendTask = Task.Run(
					() =>
					{
						for (int i=0; i < transferCount; i++)
						{
							int remaining = bytesPerTransfer;
							int offset = i * bytesPerTransfer;

							while (remaining > 0)
							{
								int bytesSent = sender.Send(testBytes, offset, remaining, SocketFlags.None);

								offset += bytesSent;
								remaining -= bytesSent;
							}
							
							Thread.Sleep(100);
						}
					});

				sut.ReceiveFromSocket(receiver, totalBytes);

				// Assert
				sendTask.Wait(TimeSpan.FromSeconds(0.2)).Should().BeTrue();

				sut.Length.Should().Be(totalBytes);

				sut._buffer!.Skip(sut._offset).Take(sut._length)
					.Should().BeEquivalentTo(testBytes, options => options.WithStrictOrdering());
			}
			finally
			{
				listener?.Dispose();
				sender?.Dispose();
				receiver?.Dispose();
			}
		}
	}
}
