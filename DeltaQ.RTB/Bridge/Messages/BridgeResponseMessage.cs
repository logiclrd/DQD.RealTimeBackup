using System;
using System.Collections.Generic;

using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge.Messages
{
	public abstract class BridgeResponseMessage : BridgeMessage
	{
		public ErrorInfo? Error;

		protected abstract void SerializeResponseImplementation(ByteBuffer buffer);
		protected abstract void DeserializeResponseImplementation(ByteBuffer buffer);

		protected sealed override void SerializeImplementation(ByteBuffer buffer)
		{
			SerializeResponseImplementation(buffer);
			SerializeError(buffer, Error);
		}

		protected sealed override void DeserializeImplementation(ByteBuffer buffer)
		{
			DeserializeResponseImplementation(buffer);

			Error = DeserializeError(buffer);
		}

		protected void SerializeError(ByteBuffer buffer) => SerializeError(buffer, Error);

		private void SerializeError(ByteBuffer buffer, ErrorInfo? error)
		{
			if (error == null)
				buffer.AppendByte(0);
			else
			{
				buffer.AppendByte(1);

				SerializeString(buffer, error.Message);
				SerializeString(buffer, error.Source);
				SerializeString(buffer, error.StackTrace);
				SerializeString(buffer, error.Message);
				SerializeError(buffer, error.InnerError);

				if (error.InnerErrors == null)
					buffer.AppendByte(0);
				else
				{
					buffer.AppendByte(1);

					buffer.AppendInt32(error.InnerErrors.Count);

					foreach (var innerError in error.InnerErrors)
						SerializeError(buffer, innerError);
				}
			}
		}

		protected ErrorInfo? DeserializeError(ByteBuffer buffer)
		{
			if (buffer.ReadByte() == 0)
				return null;
			else
			{
				var error = new ErrorInfo();

				error.Message = buffer.ReadString();
				error.Source = buffer.ReadString();
				error.StackTrace = buffer.ReadString();
				error.Message = buffer.ReadString();

				error.InnerError = DeserializeError(buffer);

				if (buffer.ReadByte() != 0)
				{
					error.InnerErrors = new List<ErrorInfo?>();

					int innerErrorCount = buffer.ReadInt32();

					for (int i=0; i < innerErrorCount; i++)
						error.InnerErrors.Add(DeserializeError(buffer));
				}

				return error;
			}
		}
	}
}
