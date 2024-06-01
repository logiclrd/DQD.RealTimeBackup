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

					_serializationPlans[type] = plan;
				}

				return plan;
			}
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
			}
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

		static void SerializeString(ByteBuffer buffer, object? value)
		{
			buffer.AppendString((string)value!);
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
			}
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

		static object? DeserializeString(ByteBuffer buffer)
		{
			return buffer.ReadString();
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
				CreatePlan(type).Serialize(buffer, instance);
			}
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
			{
				var instance = Activator.CreateInstance(type)!;

				CreatePlan(type).Deserialize(buffer, instance);

				return instance;
			}
		}
	}
}
