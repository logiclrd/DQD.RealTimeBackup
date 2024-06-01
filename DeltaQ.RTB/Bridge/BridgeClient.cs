using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

using DeltaQ.RTB.Bridge.Messages;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.Bridge
{
	public class BridgeClient : IDisposable
	{
		object _sync = new object();
		Socket _socket;

		private BridgeClient(Socket socket)
		{
			_socket = socket;
		}

		public static BridgeClient ConnectTo(string unixEndPoint)
		{
			Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

			socket.Connect(new UnixDomainSocketEndPoint(unixEndPoint));

			return new BridgeClient(socket);
		}

		public static BridgeClient ConnectTo(IPEndPoint ipEndPoint)
		{
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			socket.Connect(ipEndPoint);

			return new BridgeClient(socket);
		}

		public void Dispose()
		{
			_socket.Dispose();
		}

		void DebugLog(string line)
		{
			using (var writer = new StreamWriter("/tmp/DeltaQ.RTB.bridge.client.log", append: true))
				writer.WriteLine(line);
		}

		void DebugLog(string format, params object?[] args)
		{
			DebugLog(string.Format(format, args));
		}

		void DebugLog(object? value)
		{
			if (value != null)
				DebugLog(value.ToString()!);
		}

		public BridgeMessage? SendRequestAndReceiveResponse(BridgeMessage request)
		{
			var buffer = new ByteBuffer();

			request.SerializeWithLengthPrefix(buffer);

			DebugLog("Serialized request:");

			var builder = new StringBuilder();
			for (int i=0; i < buffer.Length; i++)
				builder.AppendFormat(" {0:X2}", buffer[i]);
			
			DebugLog(builder);

			lock (_sync)
			{
				buffer.SendFullyToSocket(_socket);

				buffer.Clear();

				buffer.ReceiveFromSocket(_socket, count: 4);

				if (buffer.TryPeekInt32(out var messageSize))
				{
					buffer.ReceiveFromSocket(_socket, messageSize);

					if (!BridgeMessage.DeserializeWithLengthPrefix(buffer, out var message))
						throw new Exception($"Could not deserialize the bridge message that was received ({messageSize} bytes)");

					return message;
				}
			}

			return null;
		}
	}
}
