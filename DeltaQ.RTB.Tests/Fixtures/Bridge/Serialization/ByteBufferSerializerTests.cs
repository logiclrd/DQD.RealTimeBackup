using System;
using System.Collections.Generic;
using System.Text;

using NUnit.Framework;

using FluentAssertions;

using DeltaQ.RTB.Bridge.Serialization;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Tests.Fixtures.Bridge.Serialization
{
	[TestFixture]
	public class ByteBufferSerializerTests
	{
#pragma warning disable 649
		class BoolFieldTestClass
		{
			[FieldOrder(0)]
			public bool TestField;
		}
#pragma warning restore 649

		[TestCase(false)]
		[TestCase(true)]
		public void Serialize_should_handle_bool_fields(bool value)
		{
			// Arrange
			var testObject = new BoolFieldTestClass();

			testObject.TestField = value;

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) field value            SHOULD BE value ? 1 : 0

			byte expectedOutputByte = value ? (byte)1 : (byte)0;

			// Act
			sut.Serialize<BoolFieldTestClass>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(2);
			buffer[0].Should().Be(1);
			buffer[1].Should().Be(expectedOutputByte);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void Deserialize_should_handle_bool_fields(bool value)
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			buffer.AppendByte(1); // is object non-null?
			buffer.AppendByte(value ? (byte)1 : (byte)0); // TestField value

			// Act
			var result = sut.Deserialize<BoolFieldTestClass>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.TestField.Should().Be(value);
		}

#pragma warning disable 649
		class IntFieldTestClass
		{
			[FieldOrder(0)]
			public int TestField;
		}
#pragma warning restore 649

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(10)]
		[TestCase(int.MaxValue)]
		[TestCase(int.MinValue)]
		public void Serialize_should_handle_int_fields(int value)
		{
			// Arrange
			var testObject = new IntFieldTestClass();

			testObject.TestField = value;

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte[4]) field value         SHOULD BE value bytes

			byte[] expectedOutputBytes = BitConverter.GetBytes(value);

			// Act
			sut.Serialize<IntFieldTestClass>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(5);
			buffer[0].Should().Be(1);

			for (int i = 0; i < expectedOutputBytes.Length; i++)
				buffer[i + 1].Should().Be(expectedOutputBytes[i]);
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(10)]
		[TestCase(int.MaxValue)]
		[TestCase(int.MinValue)]
		public void Deserialize_should_handle_int_fields(int value)
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			byte[] valueBytes = BitConverter.GetBytes(value);

			buffer.AppendByte(1); // is object non-null?
			buffer.Append(valueBytes); // TestField value

			// Act
			var result = sut.Deserialize<IntFieldTestClass>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.TestField.Should().Be(value);
		}

#pragma warning disable 649
		class LongFieldTestClass
		{
			[FieldOrder(0)]
			public long TestField;
		}
#pragma warning restore 649

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(10)]
		[TestCase(unchecked((long)0xDEADBEEFCAFEBABE))]
		[TestCase(long.MaxValue)]
		[TestCase(long.MinValue)]
		public void Serialize_should_handle_long_fields(long value)
		{
			// Arrange
			var testObject = new LongFieldTestClass();

			testObject.TestField = value;

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte[8]) field value         SHOULD BE value bytes

			byte[] expectedOutputBytes = BitConverter.GetBytes(value);

			// Act
			sut.Serialize<LongFieldTestClass>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(9);
			buffer[0].Should().Be(1);

			for (int i = 0; i < expectedOutputBytes.Length; i++)
				buffer[i + 1].Should().Be(expectedOutputBytes[i]);
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(10)]
		[TestCase(unchecked((long)0xDEADBEEFCAFEBABE))]
		[TestCase(long.MaxValue)]
		[TestCase(long.MinValue)]
		public void Deserialize_should_handle_long_fields(long value)
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			byte[] valueBytes = BitConverter.GetBytes(value);

			buffer.AppendByte(1); // is object non-null?
			buffer.Append(valueBytes); // TestField value

			// Act
			var result = sut.Deserialize<LongFieldTestClass>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.TestField.Should().Be(value);
		}

#pragma warning disable 649
		class DateTimeFieldTestClass
		{
			[FieldOrder(0)]
			public DateTime TestField;
		}
#pragma warning restore 649

		[TestCase(0L /* DateTime.MinValue */, DateTimeKind.Unspecified)]
		[TestCase(3155378975999999999 /* DateTime.MaxValue */, DateTimeKind.Unspecified)]
		[TestCase(638531247199120478 /* Tuesday, June 4, 2024 7:05:19 p.m. */, DateTimeKind.Unspecified)]
		[TestCase(638531247199120478 /* Tuesday, June 4, 2024 7:05:19 p.m. */, DateTimeKind.Local)]
		[TestCase(638531247199120478 /* Tuesday, June 4, 2024 7:05:19 p.m. */, DateTimeKind.Utc)]
		public void Serialize_should_handle_DateTime_fields(long ticks, DateTimeKind kind)
		{
			// Arrange
			var testObject = new DateTimeFieldTestClass();

			testObject.TestField = new DateTime(ticks, kind);

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte[8]) field value         SHOULD BE value bytes

			long binaryValue = testObject.TestField.ToBinary() | (((long)testObject.TestField.Kind) << 62);

			byte[] expectedOutputBytes = BitConverter.GetBytes(binaryValue);

			// Act
			sut.Serialize<DateTimeFieldTestClass>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(9);
			buffer[0].Should().Be(1);

			for (int i = 0; i < expectedOutputBytes.Length; i++)
				buffer[i + 1].Should().Be(expectedOutputBytes[i]);
		}

		[TestCase(0L /* DateTime.MinValue */, DateTimeKind.Unspecified)]
		[TestCase(3155378975999999999 /* DateTime.MaxValue */, DateTimeKind.Unspecified)]
		[TestCase(638531247199120478 /* Tuesday, June 4, 2024 7:05:19 p.m. */, DateTimeKind.Unspecified)]
		[TestCase(638531247199120478 /* Tuesday, June 4, 2024 7:05:19 p.m. */, DateTimeKind.Local)]
		[TestCase(638531247199120478 /* Tuesday, June 4, 2024 7:05:19 p.m. */, DateTimeKind.Utc)]
		public void Deserialize_should_handle_DateTime_fields(long ticks, DateTimeKind kind)
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			var expectedValue = new DateTime(ticks, kind);

			long binaryValue = expectedValue.ToBinary() | (((long)kind) << 62);

			byte[] valueBytes = BitConverter.GetBytes(binaryValue);

			buffer.AppendByte(1); // is object non-null?
			buffer.Append(valueBytes); // TestField value

			// Act
			var result = sut.Deserialize<DateTimeFieldTestClass>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.TestField.Should().Be(expectedValue);
		}

		[TestCase(0L /* DateTime.MinValue */, DateTimeKind.Unspecified)]
		[TestCase(3155378975999999999 /* DateTime.MaxValue */, DateTimeKind.Unspecified)]
		[TestCase(638531247199120478 /* Tuesday, June 4, 2024 7:05:19 p.m. */, DateTimeKind.Unspecified)]
		[TestCase(638531247199120478 /* Tuesday, June 4, 2024 7:05:19 p.m. */, DateTimeKind.Local)]
		[TestCase(638531247199120478 /* Tuesday, June 4, 2024 7:05:19 p.m. */, DateTimeKind.Utc)]
		public void DateTime_values_should_roundtrip(long ticks, DateTimeKind kind)
		{
			// Arrange
			var testObject = new DateTimeFieldTestClass();

			testObject.TestField = new DateTime(ticks, kind);

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Act
			sut.Serialize<DateTimeFieldTestClass>(testObject, buffer);

			var result = sut.Deserialize<DateTimeFieldTestClass>(buffer);
		}

#pragma warning disable 649
		class TimeSpanFieldTestClass
		{
			[FieldOrder(0)]
			public TimeSpan TestField;
		}
#pragma warning restore 649

		[TestCase(long.MinValue)]
		[TestCase(long.MaxValue)]
		[TestCase(0)]
		[TestCase(10_000_000)] // one second
		[TestCase(864_000_000_000)] // one day
		public void Serialize_should_handle_TimeSpan_fields(long ticks)
		{
			// Arrange
			var testObject = new TimeSpanFieldTestClass();

			testObject.TestField = TimeSpan.FromTicks(ticks);

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte[8]) field value         SHOULD BE value bytes

			byte[] expectedOutputBytes = BitConverter.GetBytes(ticks);

			// Act
			sut.Serialize<TimeSpanFieldTestClass>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(9);
			buffer[0].Should().Be(1);

			for (int i = 0; i < expectedOutputBytes.Length; i++)
				buffer[i + 1].Should().Be(expectedOutputBytes[i]);
		}

		[TestCase(long.MinValue)]
		[TestCase(long.MaxValue)]
		[TestCase(0)]
		[TestCase(10_000_000)] // one second
		[TestCase(864_000_000_000)] // one day
		public void Deserialize_should_handle_TimeSpan_fields(long ticks)
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			var expectedValue = TimeSpan.FromTicks(ticks);

			buffer.AppendByte(1); // is object non-null?
			buffer.AppendInt64(ticks); // TestField value

			// Act
			var result = sut.Deserialize<TimeSpanFieldTestClass>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.TestField.Should().Be(expectedValue);
		}

#pragma warning disable 649
		class StringFieldTestClass
		{
			[FieldOrder(0)]
			public string? TestField;
		}
#pragma warning restore 649

		[TestCase(null)]
		[TestCase("")]
		[TestCase("A")]
		[TestCase("Test string")]
		[TestCase("おはようございます。")]
		public void Serialize_should_handle_string_fields(string? value)
		{
			// Arrange
			var testObject = new StringFieldTestClass();

			testObject.TestField = value;

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is string non-null?    SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) string length N   SHOULD BE value.Length bytes
			//   (byte[N]) string data       SHOULD BE value UTF-8 bytes

			byte[] stringDataBytes = (value == null) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(value);
			byte[] stringLengthBytes = BitConverter.GetBytes(stringDataBytes.Length);

			int expectedOverallLength = 2 + (value != null ? stringLengthBytes.Length + stringDataBytes.Length : 0);

			// Act
			sut.Serialize<StringFieldTestClass>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer[0].Should().Be(1);

			if (value == null)
				buffer[1].Should().Be(0);
			else
			{
				buffer[1].Should().Be(1);

				for (int i = 0; i < stringLengthBytes.Length; i++)
					buffer[i + 2].Should().Be(stringLengthBytes[i]);
				for (int i = 0; i < stringDataBytes.Length; i++)
					buffer[i + 6].Should().Be(stringDataBytes[i]);
			}
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("A")]
		[TestCase("Test string")]
		[TestCase("おはようございます。")]
		public void Deserialize_should_handle_string_fields(string? value)
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    1
			// (byte) is string non-null?    (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) string length N   value.Length bytes
			//   (byte[N]) string data       value UTF-8 bytes

			buffer.AppendByte(1); // is object non-null?

			if (value == null)
				buffer.AppendByte(0); // is string null?
			else
			{
				buffer.AppendByte(1); // is string non-null?

				byte[] stringBytes = Encoding.UTF8.GetBytes(value);
				byte[] stringLengthBytes = BitConverter.GetBytes(stringBytes.Length);

				buffer.Append(stringLengthBytes);
				buffer.Append(stringBytes);
			}

			// Act
			var result = sut.Deserialize<StringFieldTestClass>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.TestField.Should().Be(value);
		}

#pragma warning disable 649
		class ArrayFieldTestClass<TElement>
		{
			[FieldOrder(0)]
			public TElement?[]? Array;
		}
#pragma warning restore 649

		[Test]
		public void Serialize_should_handle_null_array_references()
		{
			// Arrange
			var testObject = new ArrayFieldTestClass<int>();

			testObject.Array = null;

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			int expectedOverallLength = 2;

			// Act
			sut.Serialize<ArrayFieldTestClass<int>>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer[0].Should().Be(1);
			buffer[1].Should().Be(0);
		}

		[Test]
		public void Serialize_should_handle_empty_arrays()
		{
			// Arrange
			var testObject = new ArrayFieldTestClass<int>();

			testObject.Array = new int[0];

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			int expectedOverallLength = 6;

			// Act
			sut.Serialize<ArrayFieldTestClass<int>>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer[0].Should().Be(1);
			buffer[1].Should().Be(1);
			buffer[2].Should().Be(0);
			buffer[3].Should().Be(0);
			buffer[4].Should().Be(0);
			buffer[5].Should().Be(0);
		}

		[Test]
		public void Serialize_should_handle_arrays_containing_values()
		{
			// Arrange
			var testObject = new ArrayFieldTestClass<int>();

			testObject.Array = new int[] { 1, 2, 3, 42 };

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			int expectedOverallLength = 22;

			// Act
			sut.Serialize<ArrayFieldTestClass<int>>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(4);
			buffer.ReadInt32().Should().Be(1);
			buffer.ReadInt32().Should().Be(2);
			buffer.ReadInt32().Should().Be(3);
			buffer.ReadInt32().Should().Be(42);
		}

#pragma warning disable 649
		class SimpleData
		{
			[FieldOrder(0)]
			public int Value;
		}
#pragma warning restore 649

		[Test]
		public void Serialize_should_handle_arrays_containing_references()
		{
			// Arrange
			var testObject = new ArrayFieldTestClass<SimpleData>();

			testObject.Array =
				new SimpleData[]
				{
					new SimpleData() { Value = 1 },
					new SimpleData() { Value = 2 },
					new SimpleData() { Value = 3 },
					new SimpleData() { Value = 42 },
				};

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*X]) array data      SHOULD BE value element bytes
			// X is 5: 1-byte null? + 4-byte value

			int expectedOverallLength = 26;

			// Act
			sut.Serialize<ArrayFieldTestClass<SimpleData>>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(4);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(1);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(2);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(3);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(42);
		}

		[Test]
		public void Serialize_should_handle_arrays_containing_null_references()
		{
			// Arrange
			var testObject = new ArrayFieldTestClass<SimpleData>();

			testObject.Array =
				new SimpleData?[]
				{
					new SimpleData() { Value = 1 },
					null,
					null,
					new SimpleData() { Value = 42 },
				};

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*X]) array data      SHOULD BE value element bytes
			// X is 5: 1-byte null? + 4-byte value

			int expectedOverallLength = 18;

			// Act
			sut.Serialize<ArrayFieldTestClass<SimpleData>>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(4);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(1);

			buffer.ReadByte().Should().Be(0);

			buffer.ReadByte().Should().Be(0);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(42);
		}

		[Test]
		public void Deserialize_should_handle_null_array_references()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			buffer.AppendByte(1); // is object non-null?
			buffer.AppendByte(0); // is array reference non-null?

			// Act
			var result = sut.Deserialize<ArrayFieldTestClass<int>>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.Array.Should().BeNull(); ;
		}

		[Test]
		public void Deserialize_should_handle_empty_arrays()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			buffer.AppendByte(1); // is object non-null?
			buffer.AppendByte(1); // is array reference non-null?
			buffer.AppendByte(0); // array length
			buffer.AppendByte(0); // array length
			buffer.AppendByte(0); // array length
			buffer.AppendByte(0); // array length

			// Act
			var result = sut.Deserialize<ArrayFieldTestClass<int>>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.Array.Should().NotBeNull();
			result.Array.Should().HaveCount(0);
		}

		[Test]
		public void Deserialize_should_handle_arrays_containing_values()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			buffer.AppendByte(1);
			buffer.AppendByte(1);
			buffer.AppendInt32(4);
			buffer.AppendInt32(1);
			buffer.AppendInt32(2);
			buffer.AppendInt32(3);
			buffer.AppendInt32(42);

			// Act
			var result = sut.Deserialize<ArrayFieldTestClass<int>>(buffer);

			// Assert
			buffer.Length.Should().Be(0);

			result.Should().NotBeNull();
			result!.Array.Should().NotBeNull();
			result.Array.Should().HaveCount(4);
			result.Array![0].Should().Be(1);
			result.Array![1].Should().Be(2);
			result.Array![2].Should().Be(3);
			result.Array![3].Should().Be(42);
		}

		[Test]
		public void Deserialize_should_handle_arrays_containing_references()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			buffer.AppendByte(1);
			buffer.AppendByte(1);
			buffer.AppendInt32(4);

			buffer.AppendByte(1);
			buffer.AppendInt32(1);

			buffer.AppendByte(1);
			buffer.AppendInt32(2);

			buffer.AppendByte(1);
			buffer.AppendInt32(3);

			buffer.AppendByte(1);
			buffer.AppendInt32(42);

			// Act
			var result = sut.Deserialize<ArrayFieldTestClass<SimpleData>>(buffer);

			// Assert
			buffer.Length.Should().Be(0);

			result.Should().NotBeNull();
			result!.Array.Should().NotBeNull();
			result.Array.Should().HaveCount(4);
			result.Array![0].Should().NotBeNull();
			result.Array[0]!.Value.Should().Be(1);
			result.Array[1].Should().NotBeNull();
			result.Array[1]!.Value.Should().Be(2);
			result.Array[2].Should().NotBeNull();
			result.Array[2]!.Value.Should().Be(3);
			result.Array[3].Should().NotBeNull();
			result.Array[3]!.Value.Should().Be(42);
		}

		[Test]
		public void Deserialize_should_handle_arrays_containing_null_references()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			buffer.AppendByte(1);
			buffer.AppendByte(1);
			buffer.AppendInt32(4);

			buffer.AppendByte(1);
			buffer.AppendInt32(1);

			buffer.AppendByte(0);

			buffer.AppendByte(0);

			buffer.AppendByte(1);
			buffer.AppendInt32(42);

			// Act
			var result = sut.Deserialize<ArrayFieldTestClass<SimpleData>>(buffer);

			// Assert
			buffer.Length.Should().Be(0);

			result.Should().NotBeNull();
			result!.Array.Should().NotBeNull();
			result.Array.Should().HaveCount(4);
			result.Array![0].Should().NotBeNull();
			result.Array[0]!.Value.Should().Be(1);
			result.Array[1].Should().BeNull();
			result.Array[2].Should().BeNull();
			result.Array[3].Should().NotBeNull();
			result.Array[3]!.Value.Should().Be(42);
		}

#pragma warning disable 649
		class ListFieldTestClass<TElement>
		{
			[FieldOrder(0)]
			public List<TElement?>? List;
		}
#pragma warning restore 649

		[Test]
		public void Serialize_should_handle_null_list_references()
		{
			// Arrange
			var testObject = new ListFieldTestClass<int>();

			testObject.List = null;

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			int expectedOverallLength = 2;

			// Act
			sut.Serialize<ListFieldTestClass<int>>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer[0].Should().Be(1);
			buffer[1].Should().Be(0);
		}

		[Test]
		public void Serialize_should_handle_empty_lists()
		{
			// Arrange
			var testObject = new ListFieldTestClass<int>();

			testObject.List = new List<int>();

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			int expectedOverallLength = 6;

			// Act
			sut.Serialize<ListFieldTestClass<int>>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer[0].Should().Be(1);
			buffer[1].Should().Be(1);
			buffer[2].Should().Be(0);
			buffer[3].Should().Be(0);
			buffer[4].Should().Be(0);
			buffer[5].Should().Be(0);
		}

		[Test]
		public void Serialize_should_handle_lists_containing_values()
		{
			// Arrange
			var testObject = new ListFieldTestClass<int>();

			testObject.List = new List<int>() { 1, 2, 3, 42 };

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			int expectedOverallLength = 22;

			// Act
			sut.Serialize<ListFieldTestClass<int>>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(4);
			buffer.ReadInt32().Should().Be(1);
			buffer.ReadInt32().Should().Be(2);
			buffer.ReadInt32().Should().Be(3);
			buffer.ReadInt32().Should().Be(42);
		}

		[Test]
		public void Serialize_should_handle_lists_containing_references()
		{
			// Arrange
			var testObject = new ListFieldTestClass<SimpleData>();

			testObject.List =
				new List<SimpleData?>()
				{
					new SimpleData() { Value = 1 },
					new SimpleData() { Value = 2 },
					new SimpleData() { Value = 3 },
					new SimpleData() { Value = 42 },
				};

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*X]) array data      SHOULD BE value element bytes
			// X is 5: 1-byte null? + 4-byte value

			int expectedOverallLength = 26;

			// Act
			sut.Serialize<ListFieldTestClass<SimpleData>>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(4);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(1);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(2);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(3);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(42);
		}

		[Test]
		public void Serialize_should_handle_lists_containing_null_references()
		{
			// Arrange
			var testObject = new ListFieldTestClass<SimpleData>();

			testObject.List =
				new List<SimpleData?>()
				{
					new SimpleData() { Value = 1 },
					null,
					null,
					new SimpleData() { Value = 42 },
				};

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*X]) array data      SHOULD BE value element bytes
			// X is 5: 1-byte null? + 4-byte value

			int expectedOverallLength = 18;

			// Act
			sut.Serialize<ListFieldTestClass<SimpleData>>(testObject, buffer);

			// Assert
			buffer.Length.Should().Be(expectedOverallLength);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(4);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(1);

			buffer.ReadByte().Should().Be(0);

			buffer.ReadByte().Should().Be(0);

			buffer.ReadByte().Should().Be(1);
			buffer.ReadInt32().Should().Be(42);
		}

		[Test]
		public void Deserialize_should_handle_null_list_references()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			buffer.AppendByte(1); // is object non-null?
			buffer.AppendByte(0); // is list reference non-null?

			// Act
			var result = sut.Deserialize<ListFieldTestClass<int>>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.List.Should().BeNull(); ;
		}

		[Test]
		public void Deserialize_should_handle_empty_lists()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			buffer.AppendByte(1); // is object non-null?
			buffer.AppendByte(1); // is array reference non-null?
			buffer.AppendByte(0); // array length
			buffer.AppendByte(0); // array length
			buffer.AppendByte(0); // array length
			buffer.AppendByte(0); // array length

			// Act
			var result = sut.Deserialize<ListFieldTestClass<int>>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.List.Should().NotBeNull();
			result.List.Should().HaveCount(0);
		}

		[Test]
		public void Deserialize_should_handle_lists_containing_values()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			buffer.AppendByte(1);
			buffer.AppendByte(1);
			buffer.AppendInt32(4);
			buffer.AppendInt32(1);
			buffer.AppendInt32(2);
			buffer.AppendInt32(3);
			buffer.AppendInt32(42);

			// Act
			var result = sut.Deserialize<ListFieldTestClass<int>>(buffer);

			// Assert
			buffer.Length.Should().Be(0);

			result.Should().NotBeNull();
			result!.List.Should().NotBeNull();
			result.List.Should().HaveCount(4);
			result.List![0].Should().Be(1);
			result.List![1].Should().Be(2);
			result.List![2].Should().Be(3);
			result.List![3].Should().Be(42);
		}

		[Test]
		public void Deserialize_should_handle_lists_containing_references()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			buffer.AppendByte(1);
			buffer.AppendByte(1);
			buffer.AppendInt32(4);

			buffer.AppendByte(1);
			buffer.AppendInt32(1);

			buffer.AppendByte(1);
			buffer.AppendInt32(2);

			buffer.AppendByte(1);
			buffer.AppendInt32(3);

			buffer.AppendByte(1);
			buffer.AppendInt32(42);

			// Act
			var result = sut.Deserialize<ListFieldTestClass<SimpleData>>(buffer);

			// Assert
			buffer.Length.Should().Be(0);

			result.Should().NotBeNull();
			result!.List.Should().NotBeNull();
			result.List.Should().HaveCount(4);
			result.List![0].Should().NotBeNull();
			result.List[0]!.Value.Should().Be(1);
			result.List[1].Should().NotBeNull();
			result.List[1]!.Value.Should().Be(2);
			result.List[2].Should().NotBeNull();
			result.List[2]!.Value.Should().Be(3);
			result.List[3].Should().NotBeNull();
			result.List[3]!.Value.Should().Be(42);
		}

		[Test]
		public void Deserialize_should_handle_lists_containing_null_references()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is object non-null?    SHOULD BE 1
			// (byte) is array non-null?     SHOULD BE (value == null) ? 0 : 1
			// IF value != null:
			//   (byte[4]) array length N    SHOULD BE value.Length bytes
			//   (byte[N*4]) array data      SHOULD BE value element bytes

			buffer.AppendByte(1);
			buffer.AppendByte(1);
			buffer.AppendInt32(4);

			buffer.AppendByte(1);
			buffer.AppendInt32(1);

			buffer.AppendByte(0);

			buffer.AppendByte(0);

			buffer.AppendByte(1);
			buffer.AppendInt32(42);

			// Act
			var result = sut.Deserialize<ListFieldTestClass<SimpleData>>(buffer);

			// Assert
			buffer.Length.Should().Be(0);

			result.Should().NotBeNull();
			result!.List.Should().NotBeNull();
			result.List.Should().HaveCount(4);
			result.List![0].Should().NotBeNull();
			result.List[0]!.Value.Should().Be(1);
			result.List[1].Should().BeNull();
			result.List[2].Should().BeNull();
			result.List[3].Should().NotBeNull();
			result.List[3]!.Value.Should().Be(42);
		}

#pragma warning disable 649
		public class ChildObjectTestParentClass
		{
			[FieldOrder(0)]
			public ChildObjectTestChildClass? Child;
		}

		public class ChildObjectTestChildClass
		{
			[FieldOrder(0)]
			public ChildObjectTestGrandchildClass? Grandchild;
		}

		public class ChildObjectTestGrandchildClass
		{
			[FieldOrder(0)]
			public int Field;
		}
#pragma warning restore 649

		[Test]
		public void Serialize_should_handle_child_objects()
		{
			// Arrange
			var graph = new ChildObjectTestParentClass();

			graph.Child = new ChildObjectTestChildClass();
			graph.Child.Grandchild = new ChildObjectTestGrandchildClass();
			graph.Child.Grandchild.Field = 42;

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is parent non-null?     SHOULD BE 1
			// (byte) is child non-null?      SHOULD BE 1
			// (byte) is grandchild non-null? SHOULD BE 1
			// (byte[4]) field in grandchild  SHOULD BE (42, 0, 0, 0)

			// Act
			sut.Serialize(graph, buffer);

			// Assert
			buffer.Length.Should().Be(7);

			buffer[0].Should().Be(1);
			buffer[1].Should().Be(1);
			buffer[2].Should().Be(1);
			buffer[3].Should().Be(42);
			buffer[4].Should().Be(0);
			buffer[5].Should().Be(0);
			buffer[6].Should().Be(0);
		}

		[Test]
		public void Serialize_should_handle_null_object_references()
		{
			// Arrange
			var graph = new ChildObjectTestParentClass();

			graph.Child = new ChildObjectTestChildClass();
			graph.Child.Grandchild = null;

			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// Expected output:
			// (byte) is parent non-null?     SHOULD BE 1
			// (byte) is child non-null?      SHOULD BE 1
			// (byte) is grandchild non-null? SHOULD BE 0

			// Act
			sut.Serialize(graph, buffer);

			// Assert
			buffer.Length.Should().Be(3);

			buffer[0].Should().Be(1);
			buffer[1].Should().Be(1);
			buffer[2].Should().Be(0);
		}

		[Test]
		public void Deserialize_should_handle_child_objects()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is parent non-null?     1
			// (byte) is child non-null?      1
			// (byte) is grandchild non-null? 1
			// (byte[4]) field in grandchild  (42, 0, 0, 0)

			buffer.AppendByte(1); // is parent object non-null?
			buffer.AppendByte(1); // is child object non-null?
			buffer.AppendByte(1); // is grandchild object non-null?
			buffer.AppendByte(42); // integer value
			buffer.AppendByte(0);  //
			buffer.AppendByte(0);  //
			buffer.AppendByte(0);  //

			// Act
			var result = sut.Deserialize<ChildObjectTestParentClass>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.Child.Should().NotBeNull();
			result!.Child!.Grandchild.Should().NotBeNull();
			result!.Child!.Grandchild!.Field.Should().Be(42);
		}

		[Test]
		public void Deserialize_should_handle_null_object_references()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			var buffer = new ByteBuffer();

			// (byte) is parent non-null?     1
			// (byte) is child non-null?      1
			// (byte) is grandchild non-null? 0

			buffer.AppendByte(1); // is parent object non-null?
			buffer.AppendByte(1); // is child object non-null?
			buffer.AppendByte(0); // is grandchild object non-null?

			// Act
			var result = sut.Deserialize<ChildObjectTestParentClass>(buffer);

			// Assert
			result.Should().NotBeNull();
			result!.Child.Should().NotBeNull();
			result!.Child!.Grandchild.Should().BeNull();
		}

#pragma warning disable 649
		class FieldOrderTestClass
		{
			[FieldOrder(1)] public int Apples;
			[FieldOrder(0)] public int Bananas;
			[FieldOrder(2)] public int Cherries;
			[FieldOrder(-1)] public int Dates;
		}
#pragma warning restore 649

		[Test]
		public void CreatePlan_should_respect_field_order()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			// Act
			var plan = sut.CreatePlan<FieldOrderTestClass>();

			// Assert
			plan.Elements.Should().HaveCount(4);
			plan.Elements[0].FieldInfo.Name.Should().Be(nameof(FieldOrderTestClass.Dates));
			plan.Elements[1].FieldInfo.Name.Should().Be(nameof(FieldOrderTestClass.Bananas));
			plan.Elements[2].FieldInfo.Name.Should().Be(nameof(FieldOrderTestClass.Apples));
			plan.Elements[3].FieldInfo.Name.Should().Be(nameof(FieldOrderTestClass.Cherries));
		}

#pragma warning disable 649
		class UnattributedFieldTestClass
		{
			[FieldOrder(0)]
			public int A;

			public int B;

			[FieldOrder(1)]
			public int C;
		}
#pragma warning restore 649

		[Test]
		public void CreatePlan_should_ignore_unattributed_fields()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			// Act
			var plan = sut.CreatePlan<UnattributedFieldTestClass>();

			// Assert
			plan.Elements.Should().HaveCount(2);
			plan.Elements[0].FieldInfo.Name.Should().Be(nameof(UnattributedFieldTestClass.A));
			plan.Elements[1].FieldInfo.Name.Should().Be(nameof(UnattributedFieldTestClass.C));
		}

		[Test]
		public void CreatePlan_should_cache_plans()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			// Act
			var intPlan1 = sut.CreatePlan<IntFieldTestClass>();
			var stringPlan1 = sut.CreatePlan<StringFieldTestClass>();
			var objPlan1 = sut.CreatePlan<ChildObjectTestParentClass>();

			var intPlan2 = sut.CreatePlan<IntFieldTestClass>();
			var stringPlan2 = sut.CreatePlan<StringFieldTestClass>();
			var objPlan2 = sut.CreatePlan<ChildObjectTestParentClass>();

			// Assert
			intPlan2.Should().BeSameAs(intPlan1);
			stringPlan2.Should().BeSameAs(stringPlan1);
			objPlan2.Should().BeSameAs(objPlan1);
		}

#pragma warning disable 649
		public class BaseClass
		{
			[FieldOrder(0)]
			public int InheritedField;
		}

		public class Subclass : BaseClass
		{
		}
#pragma warning restore 649

		[Test]
		public void CreatePlan_should_see_inherited_fields()
		{
			// Arrange
			var sut = new ByteBufferSerializer();

			// Act
			var plan = sut.CreatePlan<Subclass>();

			// Assert
			plan.Elements.Should().NotBeEmpty();
		}
	}
}
