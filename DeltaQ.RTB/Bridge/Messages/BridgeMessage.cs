using System;
using System.Collections.Generic;
using System.Linq;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public abstract class BridgeMessage
	{
		public abstract BridgeMessageType MessageType { get; }

		public void SerializeWithLengthPrefix(ByteBuffer buffer)
		{
			int lengthOffset = buffer.Length;

			buffer.AppendInt32(0); // Dummy value, to be replaced

			int lengthBefore = buffer.Length;

			Serialize(buffer);

			int lengthAfter = buffer.Length;

			int messageLength = lengthAfter - lengthBefore;

			byte[] messageLengthBytes = BitConverter.GetBytes(messageLength);

			for (int i=0; i < 4; i++)
				buffer[lengthOffset + i] = messageLengthBytes[i];
		}

		public void Serialize(ByteBuffer buffer)
		{
			buffer.AppendInt32((int)MessageType);

			SerializeImplementation(buffer);
		}

		static Dictionary<int, Type> s_bridgeMessageTypes =
			typeof(BridgeMessage).Assembly.GetTypes()
			.Where(type => !type.IsAbstract && typeof(BridgeMessage).IsAssignableFrom(type))
			.Select(type => (BridgeMessage)Activator.CreateInstance(type)!)
			.ToDictionary(
				keySelector: instance => (int)instance.MessageType,
				elementSelector: instance => instance.GetType());

		public static bool DeserializeWithLengthPrefix(ByteBuffer buffer, out BridgeMessage message)
		{
			message = default!;

			if (!buffer.TryPeekInt32(out var messageLength))
				return false;
			if (buffer.Length - 4 < messageLength)
				return false;

			buffer.Consume(4);

			int messageType = buffer.ReadInt32();

			messageLength -= 4;

			if (!s_bridgeMessageTypes.TryGetValue(messageType, out var type)
			 || (type == null))
			{
				buffer.Consume(messageLength - 4);
				throw new Exception("Unrecognized message type: " + messageType);
			}
			else
			{
				int lengthBefore = buffer.Length;

				message = (BridgeMessage)Activator.CreateInstance(type)!;
				message.DeserializeImplementation(buffer);

				int lengthAfter = buffer.Length;

				int bytesConsumed = lengthBefore - lengthAfter;

				if (bytesConsumed > messageLength)
					throw new Exception($"Protocol error: {type.Name}.Deserialize consumed {bytesConsumed} bytes, but the message length prefix was only {messageLength}");

				if (bytesConsumed < messageLength)
					buffer.Consume(messageLength - bytesConsumed);

				return true;
			}
		}

		protected abstract void SerializeImplementation(ByteBuffer buffer);
		protected abstract void DeserializeImplementation(ByteBuffer buffer);

		protected void SerializeString(ByteBuffer buffer, string? str)
		{
			if (str == null)
				buffer.AppendByte(0);
			else
			{
				buffer.AppendByte(1);
				buffer.AppendString(str);
			}
		}

		protected string? DeserializeString(ByteBuffer buffer)
		{
			if (buffer.ReadByte() == 0)
				return null;
			else
				return buffer.ReadString();
		}
	}
}
