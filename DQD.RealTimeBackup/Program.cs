﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Serialization;

using Autofac;

using Bytewizer.Backblaze.Client;

using DQD.Backblaze.Agent.Autofac;

using DQD.CommandLineParser;

using DQD.RealTimeBackup.ActivityMonitor;
using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.Bridge;
using DQD.RealTimeBackup.Bridge.Notifications;
using DQD.RealTimeBackup.Bridge.Processors;
using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.FileSystem;
using DQD.RealTimeBackup.Scan;
using DQD.RealTimeBackup.Interop;
using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.SurfaceArea;
using DQD.RealTimeBackup.Utility;

using Timer = DQD.RealTimeBackup.Utility.Timer;

namespace DQD.RealTimeBackup
{
	class Program
	{
		static OperatingParameters BuildOperatingParameters(CommandLineArguments args)
		{
			var serializer = new XmlSerializer(typeof(OperatingParameters));

			OperatingParameters parameters;

			if (!File.Exists(args.ConfigurationPath))
				parameters = new OperatingParameters();
			else
			{
				try
				{
					using (var stream = File.OpenRead(args.ConfigurationPath))
						parameters = (OperatingParameters)serializer.Deserialize(stream)!;
				}
				catch (Exception e)
				{
					throw new Exception("Unable to parse configuration file: " + args.ConfigurationPath, e);
				}
			}

			if (args.Quiet)
				parameters.Verbosity = Verbosity.Quiet;
			if (args.Verbose)
				parameters.Verbosity = Verbosity.Verbose;
			if (args.DisableFAN || args.InitialBackupThenExit)
				parameters.EnableFileAccessNotify = false;

			if (args.ErrorLogFilePath != null)
				parameters.ErrorLogFilePath = args.ErrorLogFilePath;

			if (args.FileAccessNotifyDebugLogPath != null)
				parameters.FileAccessNotifyDebugLogPath = args.FileAccessNotifyDebugLogPath;
			if (args.RemoteFileStateCacheDebugLogPath != null)
				parameters.RemoteFileStateCacheDebugLogPath = args.RemoteFileStateCacheDebugLogPath;

			if (args.WriteConfig != null)
			{
				Console.WriteLine("Creating file: {0}", args.WriteConfig);

				try
				{
					using (var stream = File.OpenWrite(args.WriteConfig))
						serializer.Serialize(stream, parameters);
				}
				catch (Exception e)
				{
					throw new Exception("Unable to write configuration file: " + args.ConfigurationPath, e);
				}
			}

			return parameters;
		}

		static IContainer InitializeContainer(OperatingParameters parameters)
		{
			var builder = new ContainerBuilder();

			builder.RegisterInstance(parameters);

			var backblazeAgentOptions =
				new ClientOptions
				{
					KeyId = parameters.RemoteStorageKeyID,
					ApplicationKey = parameters.RemoteStorageApplicationKey
				};

			builder.AddBackblazeAgent(backblazeAgentOptions);

			builder.RegisterType<ErrorLogger>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<InitialBackupOrchestrator>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<BackupAgent>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<PeriodicRescanScheduler>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<PeriodicRescanOrchestrator>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<FileAccessNotify>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<FileSystemMonitor>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<MD5Checksum>().AsImplementedInterfaces().InstancePerDependency();
			builder.RegisterType<SurfaceAreaImplementation>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<MountTable>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<StatImplementation>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<OpenByHandleAt>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<OpenFileHandles>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<RemoteFileStateCache>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<RemoteFileStateCacheStorage>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<CacheActionLog>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<ContentKeyGenerator>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<B2RemoteStorage>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<Staging>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<Timer>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<ZFS>().AsImplementedInterfaces().SingleInstance();

			builder.RegisterType<BridgeServer>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<BridgeMessageProcessor>().AsImplementedInterfaces().SingleInstance();

			builder.RegisterAssemblyTypes(typeof(Program).Assembly)
				.Where(t => typeof(IBridgeMessageProcessorImplementation).IsAssignableFrom(t) && !t.IsAbstract)
				.AsImplementedInterfaces()
				.SingleInstance();

			builder.RegisterType<NotificationBus>().AsImplementedInterfaces().SingleInstance();

			return builder.Build();
		}

		static object _outputSync = new object();
		static string? _outputPath;
		static StreamWriter? _outputFileWriter;
		static int _outputLineCount;
		static int _maxOutputLineCount;

		static void InitializeOutput(string? path, int maxLines)
		{
			if (path != null)
			{
				if (File.Exists(path))
					File.Move(path, path + ".old", overwrite: true);

				_outputPath = path;
				_outputFileWriter = new StreamWriter(_outputPath) { AutoFlush = true };
				_outputLineCount = 0;
				_maxOutputLineCount = maxLines;
			}
		}

		static void EmitOutput(TextWriter console, string line)
		{
			console.WriteLine(line);

			if (_outputFileWriter != null)
			{
				lock (_outputSync)
				{
					_outputFileWriter.WriteLine(line);
					_outputLineCount++;

					if (_outputLineCount == _maxOutputLineCount)
					{
						_outputFileWriter.Close();

						File.Move(_outputPath!, _outputPath + ".old", overwrite: true);

						_outputFileWriter = new StreamWriter(_outputPath!) { AutoFlush = true };
						_outputLineCount = 0;
					}
				}
			}
		}

		static void Output()
		{
			EmitOutput(Console.Out, "");
		}

		static void Output(object arg)
		{
			EmitOutput(Console.Out, arg.ToString() ?? "(null)");
		}

		static void Output(string line)
		{
			EmitOutput(Console.Out, line);
		}

		static void Output(string format, params object?[] args)
		{
			EmitOutput(Console.Out, string.Format(format, args));
		}

		static void OutputError()
		{
			EmitOutput(Console.Error, "");
		}

		static void OutputError(object arg)
		{
			EmitOutput(Console.Error, arg.ToString() ?? "(null)");
		}

		static void OutputError(string line)
		{
			EmitOutput(Console.Error, line);
		}

		static void OutputError(string format, params object?[] args)
		{
			EmitOutput(Console.Error, string.Format(format, args));
		}

		const PosixSignal PosixSignal_SIGUSR1 = (PosixSignal)10;
		const PosixSignal PosixSignal_SIGUSR2 = (PosixSignal)12;

		static void EmitRunningStateDump()
		{
			Output("Generating running state dump");
			Output(RunningState.Instance.ToString());
		}

		static string EmitQueuesDump(IBackupAgent agent, IZFS zfs)
		{
			string path = "/tmp/DQD.RealTimeBackup_queues_" + DateTime.UtcNow.Ticks;

			Output("Dumping queues to {0}", path);

			Directory.CreateDirectory(path);

			agent.InspectQueues(
				(queueType, queue) =>
				{
					using (var writer = new StreamWriter(Path.Combine(path, queueType.ToString())))
					foreach (var item in queue)
						writer.WriteLine(item);
				});

			using (var writer = new StreamWriter(Path.Combine(path, "ZFSSnapshots")))
			foreach (var snapshot in zfs.CurrentSnapshots)
				writer.WriteLine(snapshot);


			return path;
		}

		static int Main()
		{
			if (!Console.IsOutputRedirected)
				Console.Write("\x1B[;r\x1B[2J");

			CommandLineArguments? args = null;

			IErrorLogger errorLogger = new ErrorLogger(OperatingParameters.DefaultErrorLogFilePath);

			try
			{
				var commandLine = new CommandLine();

				args = commandLine.Parse<CommandLineArguments>();

				if (args.ShowUsage)
				{
					commandLine.ShowUsage<CommandLineArguments>(Console.Error, "dotnet DQD.RealTimeBackup.dll", detailed: true);
					return 10;
				}

				if (args.ZFSDebugLogPath != null)
					ZFSDebugLog.Enable(args.ZFSDebugLogPath);

				InitializeOutput(args.LogFilePath, args.LogFileMaxLines);

				if (args.InitialBackupThenMonitor && args.DisableFAN)
				{
					OutputError("Conflicting command-line switches: Cannot begin monitoring if the fanotify integration is disabled.");
					OutputError("=> /DISABLEFAN conflicts with /INITIALBACKUPTHENMONITOR");

					return 2;
				}

				var parameters = BuildOperatingParameters(args);

				if (args.WriteConfig != null)
					return 3;

				if (parameters.Verbose)
				{
					Output("Command-line: {0}", Environment.CommandLine);
					Output("My process ID is: {0}", Process.GetCurrentProcess().Id);
					Output();
					Output("Operating parameters:");

					foreach (var field in typeof(OperatingParameters).GetFields(BindingFlags.Instance | BindingFlags.Public))
						Output("- {0}: {1}", field.Name, field.GetValue(parameters));
				}

				var stopEvent = new ManualResetEvent(initialState: false);
				var stoppedEvent = new ManualResetEvent(initialState: false);
				var cancellationTokenSource = new CancellationTokenSource();

				void Abort()
				{
					stopEvent.Set();
					cancellationTokenSource.Cancel();
					stoppedEvent.WaitOne();
				}

				try
				{
					var scanStatusFormatter = new ScanStatusFormatter();

					Console.CancelKeyPress +=
						(sender, e) =>
						{
							if (parameters.Verbose)
								Output("Received SIGINT");

							Abort();

							if (parameters.Verbose)
								Output("Returning from SIGINT handler");
						};

					AppDomain.CurrentDomain.ProcessExit +=
						(sender, e) =>
						{
							if (parameters.Verbose)
								Output("Received SIGTERM");

							Abort();

							if (parameters.Verbose)
								Output("Returning from SIGTERM handler");
						};

					var container = InitializeContainer(parameters);

					errorLogger = container.Resolve<IErrorLogger>();

					var notificationBus = container.Resolve<INotificationBus>();

					var storage = container.Resolve<IRemoteStorage>();
					var zfs = container.Resolve<IZFS>();
					var backupAgent = container.Resolve<IBackupAgent>();
					var remoteStorage = container.Resolve<IRemoteStorage>();
					var remoteFileStateCache = container.Resolve<IRemoteFileStateCache>();
					var remoteFileStateCacheStorage = container.Resolve<IRemoteFileStateCacheStorage>();
					var periodicRescanScheduler = container.Resolve<IPeriodicRescanScheduler>();
					var periodicRescanOrchestrator = container.Resolve<IPeriodicRescanOrchestrator>();
					var bridgeServer = container.Resolve<IBridgeServer>();

					var usr1Registration = PosixSignalRegistration.Create(
						PosixSignal_SIGUSR1,
						ctx =>
						{
							ctx.Cancel = true;

							try
							{
								EmitRunningStateDump();

								notificationBus.Post(
									new Notification()
									{
										Event = StateEvent.DiagnosticOutputEvent,
										Summary = "Diagnostic Output Event",
										Message = "Running state has been written to the service log.",
									});
							}
							catch (Exception e)
							{
								notificationBus.Post(
									new Notification()
									{
										Event = StateEvent.DiagnosticOutputEvent,
										Error = new Bridge.Messages.ErrorInfo(e),
										Message = "An error occurred while writing running state to the service log.",
									});
							}
						});

					var usr2Registration = PosixSignalRegistration.Create(
						PosixSignal_SIGUSR2,
						ctx =>
						{
							ctx.Cancel = true;

							try
							{
								string path = EmitQueuesDump(backupAgent, zfs);

								notificationBus.Post(
									new Notification()
									{
										Event = StateEvent.DiagnosticOutputEvent,
										Summary = "Diagnostic Output Event",
										Message = "A snapshot of all queues has been written to " + path,
									});
							}
							catch (Exception e)
							{
								notificationBus.Post(
									new Notification()
									{
										Event = StateEvent.DiagnosticOutputEvent,
										Error = new Bridge.Messages.ErrorInfo(e),
										Message = "An error occurred while emitting a queue snapshot.",
									});
							}
						});

					using (usr1Registration)
					using (usr2Registration)
					{
						remoteFileStateCache.LoadCache();

						object scrollWindowSync = new object();

						EventHandler<DiagnosticMessage> DiagnosticOutputHandler =
							(sender, e) =>
							{
								if (e.IsVerbose && !parameters.Verbose)
									return;
								if (e.IsUnimportant && parameters.Quiet)
									return;

								lock (scrollWindowSync)
								{
									Output(e.Message);
									Console.Out.Flush();
								}
							};

						errorLogger.DiagnosticOutput += DiagnosticOutputHandler;
						backupAgent.DiagnosticOutput += DiagnosticOutputHandler;
						remoteStorage.DiagnosticOutput += DiagnosticOutputHandler;
						remoteFileStateCache.DiagnosticOutput += DiagnosticOutputHandler;
						remoteFileStateCacheStorage.DiagnosticOutput += DiagnosticOutputHandler;
						periodicRescanOrchestrator.DiagnosticOutput += DiagnosticOutputHandler;

						if (!parameters.Quiet)
							Output("Authenticating with remote storage");

						try
						{
							storage.Authenticate();
						}
						catch (Exception e)
						{
							OutputError("Authentication with remote storage failed:");
							OutputError(e);
							Output("Authentication deferred to first communication");
						}

						if (!parameters.Quiet)
							Output("Starting backup agent...");

						backupAgent.Start();

						if (!parameters.Quiet)
							Output("Starting bridge server...");

						bridgeServer.Start();

						if (!parameters.Quiet)
								Output("Processing manual submissions");

						foreach (var move in args.PathsToMove)
						{
							if (!parameters.Quiet)
								lock (scrollWindowSync)
									Output("=> MOVE '{0}' to {1}'", move.FromPath, move.ToPath);

							backupAgent.NotifyMove(move.FromPath, move.ToPath);
						}

						foreach (var pathToCheck in args.PathsToCheck)
						{
							if (!parameters.Quiet)
								lock (scrollWindowSync)
									Output("=> CHECK '{0}'", pathToCheck);

							backupAgent.CheckPath(pathToCheck);
						}

						if (args.InitialBackupThenMonitor || args.InitialBackupThenExit)
						{
							notificationBus.Post(
								new Notification()
								{
									Event = StateEvent.InitialBackupStarted,
								});

							backupAgent.PauseMonitor();

							var orchestrator = container.Resolve<IInitialBackupOrchestrator>();

							string headings = scanStatusFormatter.Headings;
							string separator = scanStatusFormatter.Separator;

							// Clear the screen.
							if (!Console.IsOutputRedirected)
								Console.Write("\x1B[;r\x1B[2J");

							int staticLineCount = 3 + backupAgent.UploadThreadCount;

							using (var scrollWindow = new ConsoleScrollWindow(firstRow: 1, lastRow: Console.WindowHeight - 3 - backupAgent.UploadThreadCount))
							{
								orchestrator.PerformInitialBackup(
									statusUpdate =>
									{
										lock (scrollWindowSync)
										{
											using (scrollWindow.Suspend())
											{
												if (!Console.IsOutputRedirected)
												{
													Console.CursorLeft = 0;
													Console.CursorTop = Console.WindowHeight - staticLineCount;
												}

												Console.WriteLine(headings);
												Console.WriteLine(separator);
												Console.WriteLine(scanStatusFormatter.ToString(statusUpdate));

												if ((statusUpdate.BackupAgentQueueSizes != null)
												&& (statusUpdate.BackupAgentQueueSizes.UploadThreads != null))
												{
													for (int i=0; i < statusUpdate.BackupAgentQueueSizes.UploadThreads.Length; i++)
													{
														if (i > 0)
															Console.WriteLine();

														string statusLine = scanStatusFormatter.ToString(statusUpdate.BackupAgentQueueSizes.UploadThreads[i], Console.WindowWidth - 1, useANSIProgressBar: !Console.IsOutputRedirected);

														Console.Write(statusLine);

														for (int j = statusLine.Length, l = Console.WindowWidth - 1; j < l; j++)
															Console.Write(' ');
													}
												}

												scrollWindow.LastRow = Console.WindowHeight - staticLineCount - 1;
											}
										}
									},
									cancellationTokenSource.Token);
							}

							if (!Console.IsOutputRedirected)
								Console.Write("\x1B[2J");

							if (args.InitialBackupThenExit)
								stopEvent.Set();
							else if (cancellationTokenSource.IsCancellationRequested)
								Output("Not continuing with startup");
							else
							{
								if (!parameters.Quiet)
								{
									Output();
									Output("Initial backup complete, switching to realtime mode");
								}

								backupAgent.UnpauseMonitor(processBufferedPaths: true);
							}

							notificationBus.Post(
								new Notification()
								{
									Event = StateEvent.InitialBackupCompleted,
								});
						}

						if (!cancellationTokenSource.IsCancellationRequested)
						{
							if (!parameters.Quiet)
								Output("Starting periodic rescan scheduler");
							periodicRescanScheduler.Start(cancellationTokenSource.Token);

							if (!parameters.Quiet)
							{
								// Clear the screen.
								if (!Console.IsOutputRedirected)
									Console.Write("\x1B[;r\x1B[2J");

								Output("Waiting for stop signal");
							}

							using (var scrollWindow = new ConsoleScrollWindow(firstRow: 1, lastRow: Console.WindowHeight - 1 - backupAgent.UploadThreadCount))
							{
								while (!cancellationTokenSource.IsCancellationRequested)
								{
									bool signalled = stopEvent.WaitOne(TimeSpan.FromSeconds(0.5));

									if (signalled)
										break;

									lock (scrollWindowSync)
									{
										using (scrollWindow.Suspend())
										{
											if (!Console.IsOutputRedirected)
											{
												Console.CursorLeft = 0;
												Console.CursorTop = Console.WindowHeight - backupAgent.UploadThreadCount;
											}

											var uploadThreads = backupAgent.GetUploadThreads();

											for (int i=0; i < uploadThreads.Length; i++)
											{
												if (i > 0)
													Console.WriteLine();

												string statusLine = scanStatusFormatter.ToString(uploadThreads[i], Console.WindowWidth - 1, useANSIProgressBar: !Console.IsOutputRedirected);

												Console.Write(statusLine);

												for (int j = statusLine.Length, l = Console.WindowWidth - 1; j < l; j++)
													Console.Write(' ');
											}

											scrollWindow.LastRow = Console.WindowHeight - backupAgent.UploadThreadCount - 1;
										}
									}
								}
							}

							// Move cursor to the bottom end of the screen, so that output doesn't interfere with
							// what's left behind after the Backup Agent exits.
							if (!Console.IsOutputRedirected)
							{
								Console.WriteLine("\x1B[{0};1H", Console.WindowHeight);
								Console.WriteLine();
							}
						}

						if (!parameters.Quiet)
							Output("Stopping periodic rescan scheduler");
						periodicRescanScheduler.Stop();

						if (!parameters.Quiet)
							Output("Stopping backup agent...");
						backupAgent.Stop();

						if (!parameters.Quiet)
							Output("Stopping bridge server...");
						bridgeServer.Stop();

						if (!parameters.Quiet)
							Output("Removing ZFS snapshots...");

						foreach (var snapshot in zfs.CurrentSnapshots)
							snapshot.Dispose();

						if (!parameters.Quiet)
							Output("All done! Goodbye :-)");
					}
				}
				finally
				{
					stoppedEvent.Set();
				}

				return 0;
			}
			catch (Exception e)
			{
				errorLogger.DisconnectDiagnosticOutput();
				errorLogger.LogError(
					"An error was caught by the exception handler in Main. This is a terminating error.",
					ErrorLogger.Summary.InternalError,
					exception: e);

				bool quiet = false;
				bool verbose = false;

				if (args != null)
				{
					quiet = args.Quiet && !args.Verbose;
					verbose = args.Verbose;
				}

				OutputError(e);

				if ((e.InnerException != null) && !quiet)
				{
					OutputError();

					if (verbose)
						OutputError(e);
					else
						OutputError("{0}: {1}", e.InnerException.GetType().Name, e.InnerException.Message);
				}

				return 1;
			}
		}
	}
}

