using System;
using System.Reflection;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Serialization
{
	public class SerializationPlanElement
	{
		public FieldInfo FieldInfo;
		public Action<ByteBuffer, object?> SerializationFunctor;
		public Func<ByteBuffer, object?> DeserializationFunctor;

		public SerializationPlanElement(FieldInfo fieldInfo, Action<ByteBuffer, object?> serializationFunctor, Func<ByteBuffer, object?> deserializationFunctor)
		{
			FieldInfo = fieldInfo;
			SerializationFunctor = serializationFunctor;
			DeserializationFunctor = deserializationFunctor;
		}
	}
}
