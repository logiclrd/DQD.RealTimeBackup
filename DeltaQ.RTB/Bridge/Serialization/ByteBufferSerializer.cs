using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Serialization
{
	public class ByteBufferSerializer
	{
		public static readonly ByteBufferSerializer Instance = new ByteBufferSerializer();

		object _sync = new object();
		Dictionary<Type, SerializationPlan> _serializationPlans = new Dictionary<Type, SerializationPlan>();

		public SerializationPlan CreatePlan<T>() => CreatePlan(typeof(T));

		public SerializationPlan CreatePlan(Type type)
		{
			lock (_sync)
			{
				if (!_serializationPlans.TryGetValue(type, out var plan))
				{
					plan = new SerializationPlan(type);

					_serializationPlans[type] = plan;

					var fields = new List<(FieldInfo FieldInfo, int Order)>();

					foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public)
					              .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)))
					{
						if (field.GetCustomAttribute<FieldOrderAttribute>() is FieldOrderAttribute fieldOrder)
							fields.Add((field, fieldOrder.Order));
					}

					fields.Sort((a, b) => a.Order.CompareTo(b.Order));

					foreach (var field in fields)
					{
						plan.Elements.Add(new SerializationPlanElement(
							field.FieldInfo,
							GetSerializationFunctorForType(field.FieldInfo.FieldType),
							GetDeserializationFunctorForType(field.FieldInfo.FieldType)));
					}
				}

				return plan;
			}
		}

		static bool IsListType(Type type)
		{
			if (type.IsGenericType)
			{
				var typeDefinition = type.GetGenericTypeDefinition();

				if (typeDefinition == typeof(List<>))
					return true;
			}

			return false;
		}

		Action<ByteBuffer, object?> GetSerializationFunctorForType(Type type)
		{
			if (type.IsValueType)
			{
				if (type == typeof(bool))
					return SerializeBool;
				if (type == typeof(int))
					return SerializeInt32;
				if (type == typeof(long))
					return SerializeInt64;
				if (type == typeof(DateTime))
					return SerializeDateTime;
				if (type == typeof(TimeSpan))
					return SerializeTimeSpan;
			}
			else if (IsListType(type))
				return SerializeList;
			else if (type.IsArray)
				return GetSerializeArrayFunctor(type.GetElementType()!);
			else
			{
				Action<ByteBuffer, object?>? valueFunctor = null;

				if (type == typeof(string))
					valueFunctor = SerializeString;
				else
				{
					var plan = CreatePlan(type);

					valueFunctor =
						(buffer, value) =>
						{
							plan.Serialize(buffer, value!);
						};
				}

				if (valueFunctor != null)
				{
					return
						(buffer, value) =>
						{
							if (value == null)
								buffer.AppendByte(0);
							else
							{
								buffer.AppendByte(1);
								valueFunctor(buffer, value);
							}
						};
				}
			}

			throw new Exception("No serialization functor available for type: " + type);
		}

		static void SerializeBool(ByteBuffer buffer, object? value)
		{
			if ((value is bool booleanValue) && booleanValue)
				buffer.AppendByte(1);
			else
				buffer.AppendByte(0);
		}

		static void SerializeInt32(ByteBuffer buffer, object? value)
		{
			if (!(value is int intValue))
				intValue = 0;

			buffer.AppendInt32(intValue);
		}

		static void SerializeInt64(ByteBuffer buffer, object? value)
		{
			if (!(value is long longValue))
				longValue = 0;

			buffer.AppendInt64(longValue);
		}

		static void SerializeDateTime(ByteBuffer buffer, object? value)
		{
			if (!(value is DateTime dateTime))
				dateTime = DateTime.MinValue;

			buffer.AppendInt64(dateTime.ToBinary());
		}

		static void SerializeTimeSpan(ByteBuffer buffer, object? value)
		{
			if (!(value is TimeSpan timeSpan))
				timeSpan = TimeSpan.Zero;

			buffer.AppendInt64(timeSpan.Ticks);
		}

		static void SerializeString(ByteBuffer buffer, object? value)
		{
			buffer.AppendString((string)value!);
		}

		static void SerializeArray(ByteBuffer buffer, object? value, Action<ByteBuffer, object?> elementFunctor)
		{
			if (!(value is Array array))
				buffer.AppendByte(0);
			else
			{
				buffer.AppendByte(1);
				buffer.AppendInt32(array.Length);

				for (int i=0; i < array.Length; i++)
					elementFunctor(buffer, array.GetValue(i));
			}
		}

		static Type GetGenericListElementType(Type genericListType)
		{
			foreach (var iface in genericListType.GetInterfaces())
				if (iface.IsGenericType && (iface.GetGenericTypeDefinition() == typeof(IList<>)))
					return iface.GetGenericArguments()[0];

			throw new Exception("Couldn't identify list element type for type: " + genericListType);
		}

		void SerializeList(ByteBuffer buffer, object? value)
		{
			if (!(value is System.Collections.IList list))
				buffer.AppendByte(0);
			else
			{
				buffer.AppendByte(1);

				var listType = value.GetType();

				var elementType = GetGenericListElementType(listType);

				var elementFunctor = GetSerializationFunctorForType(elementType);

				buffer.AppendInt32(list.Count);

				for (int i=0, l=list.Count; i < l; i++)
					elementFunctor(buffer, list[i]);
			}
		}

		Action<ByteBuffer, object?> GetSerializeArrayFunctor(Type elementType)
		{
			var elementFunctor = GetSerializationFunctorForType(elementType);

			return (buffer, value) => SerializeArray(buffer, value, elementFunctor);
		}

		Func<ByteBuffer, object?> GetDeserializationFunctorForType(Type type)
		{
			if (type.IsValueType)
			{
				if (type == typeof(bool))
					return DeserializeBool;
				if (type == typeof(int))
					return DeserializeInt32;
				if (type == typeof(long))
					return DeserializeInt64;
				if (type == typeof(DateTime))
					return DeserializeDateTime;
				if (type == typeof(TimeSpan))
					return DeserializeTimeSpan;
			}
			else if (IsListType(type))
				return GetDeserializeListFunctor(type);
			else if (type.IsArray)
				return GetDeserializeArrayFunctor(type.GetElementType()!);
			else
			{
				Func<ByteBuffer, object?>? valueFunctor = null;

				if (type == typeof(string))
					valueFunctor = DeserializeString;
				else
				{
					var plan = CreatePlan(type);

					valueFunctor =
						(buffer) =>
						{
							var instance = Activator.CreateInstance(type)!;

							plan.Deserialize(buffer, instance);

							return instance;
						};
				}

				if (valueFunctor != null)
				{
					return
						(buffer) =>
						{
							if (buffer.ReadByte() == 0)
								return null;
							else
								return valueFunctor(buffer);
						};
				}
			}

			throw new Exception("No serialization functor available for type: " + type);
		}

		static object? DeserializeBool(ByteBuffer buffer)
		{
			return (buffer.ReadByte() != 0);
		}

		static object? DeserializeInt32(ByteBuffer buffer)
		{
			return buffer.ReadInt32();
		}

		static object? DeserializeInt64(ByteBuffer buffer)
		{
			return buffer.ReadInt64();
		}

		static object? DeserializeDateTime(ByteBuffer buffer)
		{
			return DateTime.FromBinary(buffer.ReadInt64());
		}

		static object? DeserializeTimeSpan(ByteBuffer buffer)
		{
			return TimeSpan.FromTicks(buffer.ReadInt64());
		}

		static object? DeserializeString(ByteBuffer buffer)
		{
			return buffer.ReadString();
		}

		object? DeserializeList(ByteBuffer buffer, Type listType)
		{
			if (buffer.ReadByte() == 0)
				return null;

			var list = (System.Collections.IList)Activator.CreateInstance(listType)!;

			var elementType = GetGenericListElementType(listType);

			var elementFunctor = GetDeserializationFunctorForType(elementType);

			int elementCount = buffer.ReadInt32();

			for (int i=0; i < elementCount; i++)
				list.Add(elementFunctor(buffer));

			return list;
		}

		Func<ByteBuffer, object?> GetDeserializeListFunctor(Type listType)
		{
			return (buffer) => DeserializeList(buffer, listType);
		}

		static object? DeserializeArray(ByteBuffer buffer, Type elementType, Func<ByteBuffer, object?> elementFunctor)
		{
			if (buffer.ReadByte() == 0)
				return null;

			int elementCount = buffer.ReadInt32();

			var array = Array.CreateInstance(elementType, elementCount);

			for (int i=0; i < elementCount; i++)
				array.SetValue(elementFunctor(buffer), i);

			return array;
		}

		Func<ByteBuffer, object?> GetDeserializeArrayFunctor(Type elementType)
		{
			var elementFunctor = GetDeserializationFunctorForType(elementType);

			return (buffer) => DeserializeArray(buffer, elementType, elementFunctor);
		}

		public void Serialize<T>(T instance, ByteBuffer buffer)
		{
			if (instance == null)
				buffer.AppendByte(0);
			else
			{
				buffer.AppendByte(1);
				CreatePlan<T>().Serialize(buffer, instance);
			}
		}

		public void Serialize(Type type, object instance, ByteBuffer buffer)
		{
			if (instance == null)
				buffer.AppendByte(0);
			else
			{
				buffer.AppendByte(1);
				SerializeNotNull(type, instance, buffer);
			}
		}

		public void SerializeNotNull(Type type, object instance, ByteBuffer buffer)
		{
			CreatePlan(type).Serialize(buffer, instance);
		}

		public T? Deserialize<T>(ByteBuffer buffer)
			where T : class, new()
		{
			return (T?)Deserialize(typeof(T), buffer);
		}

		public object? Deserialize(Type type, ByteBuffer buffer)
		{
			if (buffer.ReadByte() == 0)
				return null;
			else
				return DeserializeNotNull(type, buffer);
		}

		public object? DeserializeNotNull(Type type, ByteBuffer buffer)
		{
			var instance = Activator.CreateInstance(type)!;

			CreatePlan(type).Deserialize(buffer, instance);

			return instance;
		}
	}
}
