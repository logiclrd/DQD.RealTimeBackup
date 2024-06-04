using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using DeltaQ.RTB.Bridge.Messages;
using DeltaQ.RTB.Bridge.Processors;
using DeltaQ.RTB.Interop;

namespace DeltaQ.RTB.Bridge
{
	public class BridgeServer : IBridgeServer
	{
		OperatingParameters _parameters;

		IBridgeMessageProcessor _messageProcessor;

		bool _shuttingDown;
		Socket? _unixSocket;
		Socket? _tcpSocket;
		object _clientSync = new object();
		Socket? _wakeSenderSocket;
		List<BridgeClientConnection> _clients = new List<BridgeClientConnection>();

		public BridgeServer(OperatingParameters parameters, IBridgeMessageProcessor messageProcessor)
		{
			_parameters = parameters;

			_messageProcessor = messageProcessor;
		}

		void DebugLog(string line)
		{
			using (var writer = new StreamWriter("/tmp/DeltaQ.RTB.bridge.server.log", append: true))
				writer.WriteLine("[{0:HH:mm:ss.fffffff}] {1}", DateTime.Now, line);
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

		public const string UNIXSocketName = "bridge.socket";
		public const string TCPPortFileName = "bridge-tcp-port";

		public void Start()
		{
			Directory.CreateDirectory(_parameters.IPCPath);

			string unixSocketPath = Path.Combine(_parameters.IPCPath, UNIXSocketName);
			string tcpPortPath = Path.Combine(_parameters.IPCPath, TCPPortFileName);

			File.Delete(unixSocketPath);
			File.Delete(tcpPortPath);

			if (_parameters.IPCUseUNIXSocket)
			{
				_unixSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

				// NB: Not threadsafe!
				int previousUmask = NativeMethods.umask(0);

				_unixSocket.Bind(new UnixDomainSocketEndPoint(unixSocketPath));

				NativeMethods.umask(previousUmask);

				_unixSocket.Listen(5);
			}

			if (_parameters.IPCUseTCPSocket)
			{
				_tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				_tcpSocket.Bind(new IPEndPoint(IPAddress.Parse(_parameters.IPCBindTCPAddress), _parameters.IPCBindTCPPortNumber));
				_tcpSocket.Listen();

				if (!(_tcpSocket.LocalEndPoint is IPEndPoint localEndPoint))
					throw new Exception("Sanity failure");

				int tcpPort = localEndPoint.Port;

				File.WriteAllText(tcpPortPath, tcpPort.ToString());
			}

			_shuttingDown = false;

			using (var wakeListenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
			{
				wakeListenerSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
				wakeListenerSocket.Listen();

				if (!(wakeListenerSocket.LocalEndPoint is IPEndPoint localEndPoint))
					throw new Exception("Sanity failure");

				StartListenerThread();
				StartClientThread(localEndPoint);

				_wakeSenderSocket = wakeListenerSocket.Accept();
			}
		}

		void StartListenerThread()
		{
			new Thread(ListenerThreadProc).Start();
		}

		void StartClientThread(IPEndPoint wakeListenerEndPoint)
		{
			new Thread(() => ClientThreadProc(wakeListenerEndPoint)).Start();
		}

		public void Stop()
		{
			_shuttingDown = true;

			_unixSocket?.Dispose();
			_unixSocket = null;

			_tcpSocket?.Dispose();
			_tcpSocket = null;

			WakeClientThreadProc();
		}

		void ListenerThreadProc()
		{
			var sockets = new List<Socket>();

			while (!_shuttingDown)
			{
				sockets.Clear();

				if (_unixSocket != null)
					sockets.Add(_unixSocket);
				if (_tcpSocket != null)
					sockets.Add(_tcpSocket);

				try
				{
					Socket.Select(checkRead: sockets, checkWrite: null, checkError: null, timeout: TimeSpan.FromSeconds(10));
				}
				catch
				{
					continue;
				}

				void AddClient(Socket client)
				{
					var connection = new BridgeClientConnection(client);

					lock (_clientSync)
					{
						_clients.Add(connection);
						Monitor.PulseAll(_clientSync);
					}
				}

				try
				{
					if ((_unixSocket != null) && sockets.Contains(_unixSocket))
						AddClient(_unixSocket.Accept());

					if ((_tcpSocket != null) && sockets.Contains(_tcpSocket))
						AddClient(_tcpSocket.Accept());
				}
				catch
				{
					if (_shuttingDown)
						break;
					else
						throw;
				}
			}
		}

		static byte[] OneByte = new byte[1];

		void WakeClientThreadProc()
		{
			_wakeSenderSocket?.Send(OneByte, 0, OneByte.Length, SocketFlags.None);

			lock (_clientSync)
				Monitor.PulseAll(_clientSync);
		}

		void ClientThreadProc(IPEndPoint wakeListenerEndPoint)
		{
			var wakeListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			wakeListener.Connect(wakeListenerEndPoint);

			var checkRead = new List<Socket>();
			var checkWrite = new List<Socket>();
			var activeClients = new List<BridgeClientConnection>();
			var clientsWithBufferedData = new List<BridgeClientConnection>();

			byte[] receiveBuffer = new byte[4096];

			while (!_shuttingDown)
			{
				checkRead.Clear();
				checkWrite.Clear();

				lock (_clientSync)
				{
					for (int i = _clients.Count - 1; i >= 0; i--)
					{
						var client = _clients[i];

						if (client.IsDead)
						{
							client.Dispose();
							_clients.RemoveAt(i);
							continue;
						}

						checkRead.Add(client.Socket);

						if (client.HasBytesToSend)
							checkWrite.Add(client.Socket);
					}

					if (checkRead.Count == 0)
					{
						Monitor.Wait(_clientSync);
						continue;
					}

					checkRead.Add(wakeListener);
				}

				try
				{
					Socket.Select(checkRead, checkWrite, checkError: null, TimeSpan.FromSeconds(10));
				}
				catch
				{
					if (_shuttingDown)
						return;
					else
						throw;
				}

				if (checkWrite.Count > 0)
				{
					activeClients.Clear();

					lock (_clientSync)
					{
						foreach (var client in _clients)
							if (checkWrite.Contains(client.Socket))
								activeClients.Add(client);
					}

					foreach (var client in activeClients)
					{
						try
						{
							lock (client)
								client.SendOnceToSocket();
						}
						catch
						{
							client.IsDead = true;
						}
					}
				}

				if (checkRead.Count > 0)
				{
					activeClients.Clear();
					clientsWithBufferedData.Clear();

					lock (_clientSync)
					{
						foreach (var client in _clients)
						{
							if (checkRead.Contains(client.Socket) && !client.IsDead)
								activeClients.Add(client);

							if (client.ReceiveBuffer.Length > 0)
								clientsWithBufferedData.Add(client);
						}
					}

					foreach (var client in activeClients)
					{
						try
						{
							int bytesRead = client.Socket.Receive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None);

							if (bytesRead == 0)
							{
								// Client disconnected.
								client.IsDead = true;
								continue;
							}

							if (client.ReceiveBuffer.Length == 0)
								clientsWithBufferedData.Add(client);

							client.ReceiveBuffer.Append(receiveBuffer, 0, bytesRead);

							DebugLog("read {0} bytes", bytesRead);
							DebugLog("receive buffer:");
							var builder = new StringBuilder();
							for (int i=0; i < client.ReceiveBuffer.Length; i++)
								builder.AppendFormat("{0:X2} ", client.ReceiveBuffer[i]);
							DebugLog(builder);
						}
						catch (Exception e)
						{
							DebugLog("exception:");
							DebugLog(e);
							client.IsDead = true;
						}
					}
				}

				foreach (var client in clientsWithBufferedData)
				{
					if (client.IsProcessingMessage)
					{
						DebugLog("Client is already processing a message, skipping for now");
						continue;
					}

					try
					{
						while (BridgeMessage.DeserializeWithLengthPrefix<BridgeRequestMessage>(client.ReceiveBuffer, out var message))
						{
							DebugLog("got a message of type {0}", message.MessageType);

							client.IsProcessingMessage = true;

							void ProcessMessage()
							{
								try
								{
									var result = _messageProcessor.ProcessMessage(message);

									if (result != null)
									{
										DebugLog("ProcessMessage returned a message of type {0}", result!.ResponseMessage!.MessageType);

										if (result.DisconnectClient)
											client.IsDead = true;

										if (result.ResponseMessage != null)
										{
											lock (_clientSync)
												result.ResponseMessage.SerializeWithLengthPrefix(client.SendBuffer);

											DebugLog("send buffer:");
											var builder = new StringBuilder();
											for (int i=0; i < client.SendBuffer.Length; i++)
												builder.AppendFormat("{0:X2} ", client.SendBuffer[i]);
											DebugLog(builder);
										}
									}
								}
								finally
								{
									client.IsProcessingMessage = false;
								}
							}

							if (!_messageProcessor.IsLongRunning(message))
							{
								DebugLog("Processing this message");

								ProcessMessage();

								DebugLog("Processing returned");

								if (client.IsDead)
									break;
							}
							else
							{
								DebugLog("Dispatching processing of this message to the thread pool");

								ThreadPool.QueueUserWorkItem(
									(state) =>
									{
										ProcessMessage();
										WakeClientThreadProc();
									});

								break;
							}
						}
					}
					catch (Exception e)
					{
						DebugLog("exception:");
						DebugLog(e);
						client.IsDead = true;
					}
				}
			}

			lock (_clientSync)
			{
				foreach (var client in _clients)
					client.Socket.Dispose();

				_clients.Clear();
			}
		}
	}
}
