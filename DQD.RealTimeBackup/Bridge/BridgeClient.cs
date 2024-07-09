// #define DEBUGLOG

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

using DQD.RealTimeBackup.Bridge.Messages;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.Bridge
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

		object _dls = new object();

		[Conditional("DEBUGLOG")]
		void DebugLog(string line)
		{
			try
			{
				lock (_dls)
					using (var writer = new StreamWriter("/tmp/DQD.RealTimeBackup.bridge.client.log", append: true))
						writer.WriteLine("[{0:HH:mm:ss.fffffff}] [{1}] {2}", DateTime.Now, System.Threading.Thread.CurrentThread.ManagedThreadId, line);
			}
			catch {}
		}

		[Conditional("DEBUGLOG")]
		void DebugLog(string format, params object?[] args)
		{
			DebugLog(string.Format(format, args));
		}

		[Conditional("DEBUGLOG")]
		void DebugLog(object? value)
		{
			if (value != null)
				DebugLog(value.ToString()!);
		}

		public BridgeResponseMessage SendRequestAndReceiveResponse(BridgeRequestMessage request)
		{
			var buffer = new ByteBuffer();

			request.SerializeWithLengthPrefix(buffer);

			DebugLog("Serialized request of type {0}:", request.GetType().Name);

			var builder = new StringBuilder();
			for (int i=0; i < buffer.Length; i++)
				builder.AppendFormat(" {0:X2}", buffer[i]);
			
			DebugLog(builder);

			lock (_sync)
			{
				buffer.SendFullyToSocket(_socket);

				buffer.Clear();

				buffer.ReceiveFromSocket(_socket, count: 4);

				if (!buffer.TryPeekInt32(out var messageSize))
					throw new Exception("Sanity failure: ReceiveFromSocket says we received 4 bytes but TryPeekInt32 returned false");

				DebugLog("Response is {0} bytes long", messageSize);

				buffer.ReceiveFromSocket(_socket, messageSize);

				DebugLog("Response received");

				if (!BridgeMessage.DeserializeWithLengthPrefix<BridgeResponseMessage>(buffer, out var message))
				{
					DebugLog("Can't deserialize");
					throw new Exception($"Could not deserialize the bridge message that was received ({messageSize} bytes)");
				}

				DebugLog("Returning message of type {0}", message.GetType().Name);

				return message;
			}
		}
	}
}
