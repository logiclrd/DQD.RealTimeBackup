using System;
using System.Net.Sockets;

using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.Bridge
{
	public class BridgeClientConnection : IDisposable
	{
		public Socket Socket;
		public ByteBuffer SendBuffer = new ByteBuffer();
		public ByteBuffer ReceiveBuffer = new ByteBuffer();

		public bool IsProcessingMessage;

		public BridgeClientConnection(Socket socket)
		{
			Socket = socket;
		}

		public bool IsDead;

		public bool HasBytesToSend => (SendBuffer.Length > 0);

		public void SendOnceToSocket()
		{
			SendBuffer.SendOnceToSocket(Socket);
		}

		public void Dispose()
		{
			Socket.Dispose();
			SendBuffer.Release();
			ReceiveBuffer.Release();
		}
	}
}
