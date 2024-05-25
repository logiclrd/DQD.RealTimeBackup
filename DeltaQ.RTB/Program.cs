using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Xml.Serialization;

using Autofac;
using DeltaQ.CommandLineParser;

using DeltaQ.RTB.ActivityMonitor;
using DeltaQ.RTB.Agent;
using DeltaQ.RTB.FileSystem;
using DeltaQ.RTB.InitialBackup;
using DeltaQ.RTB.Interop;
using DeltaQ.RTB.StateCache;
using DeltaQ.RTB.Storage;
using DeltaQ.RTB.SurfaceArea;
using DeltaQ.RTB.Utility;

using Timer = DeltaQ.RTB.Utility.Timer;

using Bytewizer.Backblaze.Client;

namespace DeltaQ.RTB
{
	class Program
	{
		static OperatingParameters BuildOperatingParameters(CommandLineArguments args)
		{
			OperatingParameters parameters;

			if (!File.Exists(args.ConfigurationPath))
				parameters = new OperatingParameters();
			else
			{
				var serializer = new XmlSerializer(typeof(OperatingParameters));

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

			builder.RegisterType<InitialBackupOrchestrator>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<BackupAgent>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<FileAccessNotify>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<FileSystemMonitor>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<MD5Checksum>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<SurfaceAreaImplementation>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<MountTable>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<StatImplementation>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<OpenByHandleAt>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<OpenFileHandles>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<RemoteFileStateCache>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<RemoteFileStateCacheStorage>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<B2RemoteStorage>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<Staging>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<Timer>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<ZFS>().AsImplementedInterfaces().SingleInstance();

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

		static int Main()
		{
			Console.Write("\x1B[;r\x1B[2J");

			CommandLineArguments? args = null;

			try
			{
				args = new CommandLine().Parse<CommandLineArguments>();

				InitializeOutput(args.LogFilePath, args.LogFileMaxLines);

				if (args.InitialBackupThenMonitor && args.DisableFAN)
				{
					OutputError("Conflicting command-line switches: Cannot begin monitoring if the fanotify integration is disabled.");
					OutputError("=> /DISABLEFAN conflicts with /INITIALBACKUPTHENMONITOR");

					return 2;
				}

				var parameters = BuildOperatingParameters(args);

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

					var backupAgent = container.Resolve<IBackupAgent>();

					object scrollWindowSync = new object();

					backupAgent.DiagnosticOutput +=
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

					if (!parameters.Quiet)
						Output("Starting backup agent...");

					backupAgent.Start();

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
						backupAgent.PauseMonitor();

						var orchestrator = container.Resolve<IInitialBackupOrchestrator>();

						string headings = InitialBackupStatus.Headings;
						string separator = InitialBackupStatus.Separator;

						// Clear the screen.
						Console.Write("\x1B[;r\x1B[2J");

						using (var scrollWindow = new ConsoleScrollWindow(firstRow: 1, lastRow: Console.WindowHeight - 3))
						{
							orchestrator.PerformInitialBackup(
								statusUpdate =>
								{
									lock (scrollWindowSync)
									{
										using (scrollWindow.Suspend())
										{
											Console.CursorLeft = 0;
											Console.CursorTop = Console.WindowHeight - 3;

											Console.WriteLine(headings);
											Console.WriteLine(separator);
											Console.Write(statusUpdate);

											scrollWindow.LastRow = Console.WindowHeight - 4;
										}
									}
								},
								cancellationTokenSource.Token);
						}

						if (args.InitialBackupThenExit)
							stopEvent.Set();
						else
						{
							if (!parameters.Quiet)
							{
								Output();
								Output("Initial backup complete, switching to realtime mode");
							}

							backupAgent.UnpauseMonitor();
						}
					}

					if (!parameters.Quiet)
						Output("Waiting for stop signal");
					stopEvent.WaitOne();

					if (!parameters.Quiet)
						Output("Stopping backup agent...");
					backupAgent.Stop();

					if (!parameters.Quiet)
						Output("All done! Goodbye :-)");
				}
				finally
				{
					stoppedEvent.Set();
				}

				return 0;
			}
			catch (Exception e)
			{
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

