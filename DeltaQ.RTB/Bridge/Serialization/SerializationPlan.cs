using System;
using System.Collections.Generic;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Serialization
{
	public class SerializationPlan
	{
		public Type Type;
		public List<SerializationPlanElement> Elements = new List<SerializationPlanElement>();

		public SerializationPlan(Type type)
		{
			Type = type;
		}

		public void Serialize(ByteBuffer buffer, object obj)
		{
			foreach (var element in Elements)
			{
				object? fieldValue = element.FieldInfo.GetValue(obj);

				element.SerializationFunctor(buffer, fieldValue);
			}
		}

		public void Deserialize(ByteBuffer buffer, object obj)
		{
			foreach (var element in Elements)
			{
				object? fieldValue = element.DeserializationFunctor(buffer);

				element.FieldInfo.SetValue(obj, fieldValue);
			}
		}
	}
}
