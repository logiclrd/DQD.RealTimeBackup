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
		const string ConfigurationPath = "/etc/DeltaQ.RTB.xml";

		static OperatingParameters BuildOperatingParameters(CommandLineArguments args)
		{
			OperatingParameters parameters;

			if (!File.Exists(ConfigurationPath))
				parameters = new OperatingParameters();
			else
			{
				var serializer = new XmlSerializer(typeof(OperatingParameters));

				try
				{
					using (var stream = File.OpenRead(ConfigurationPath))
						parameters = (OperatingParameters)serializer.Deserialize(stream)!;
				}
				catch (Exception e)
				{
					throw new Exception("Unable to parse configuration file: " + ConfigurationPath, e);
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

		static int Main()
		{
			CommandLineArguments? args = null;

			try
			{
				args = new CommandLine().Parse<CommandLineArguments>();

				if (args.InitialBackupThenMonitor && args.DisableFAN)
				{
					Console.Error.WriteLine("Conflicting command-line switches: Cannot begin monitoring if the fanotify integration is disabled.");
					Console.Error.WriteLine("=> /DISABLEFAN conflicts with /INITIALBACKUPTHENMONITOR");

					return 2;
				}

				var parameters = BuildOperatingParameters(args);

				if (parameters.Verbose)
				{
					Console.WriteLine("My process ID is: {0}", Process.GetCurrentProcess().Id);
					Console.WriteLine("Command-line: {0}", Environment.CommandLine);
					Console.WriteLine();
					Console.WriteLine("Operating parameters:");

					foreach (var field in typeof(OperatingParameters).GetFields(BindingFlags.Instance | BindingFlags.Public))
						Console.WriteLine("- {0}: {1}", field.Name, field.GetValue(parameters));
				}

				var stopEvent = new ManualResetEvent(initialState: false);
				var stoppedEvent = new ManualResetEvent(initialState: false);

				try
				{
					Console.CancelKeyPress +=
						(sender, e) =>
						{
							if (parameters.Verbose)
								Console.WriteLine("Received SIGINT");
							stopEvent.Set();
							stoppedEvent.WaitOne();
						};

					AppDomain.CurrentDomain.ProcessExit +=
						(sender, e) =>
						{
							if (parameters.Verbose)
								Console.WriteLine("Received SIGTERM");
							stopEvent.Set();
							stoppedEvent.WaitOne();
						};

					var container = InitializeContainer(parameters);

					var backupAgent = container.Resolve<IBackupAgent>();

					if (!parameters.Quiet)
						Console.WriteLine("Starting backup agent...");
					backupAgent.Start();

					foreach (var move in args.PathsToMove)
						backupAgent.NotifyMove(move.FromPath, move.ToPath);

					foreach (var pathToCheck in args.PathsToCheck)
						backupAgent.CheckPath(pathToCheck);

					if (args.InitialBackupThenMonitor || args.InitialBackupThenExit)
					{
						backupAgent.PauseMonitor();

						var orchestrator = container.Resolve<IInitialBackupOrchestrator>();

						orchestrator.PerformInitialBackup(
							statusUpdate =>
							{
								Console.CursorLeft = 0;
								Console.Write(statusUpdate);
							});

						if (args.InitialBackupThenExit)
							stopEvent.Set();
						else
						{
							Console.WriteLine();
							Console.WriteLine("Initial backup complete, switching to realtime mode");

							backupAgent.UnpauseMonitor();
						}
					}

					if (!parameters.Quiet)
						Console.WriteLine("Waiting for stop signal");
					stopEvent.WaitOne();

					if (!parameters.Quiet)
						Console.WriteLine("Stopping backup agent...");
					backupAgent.Stop();
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

				Console.Error.WriteLine(e);

				if ((e.InnerException != null) && !quiet)
				{
					Console.Error.WriteLine();

					if (verbose)
						Console.WriteLine(e);
					else
						Console.Error.WriteLine("{0}: {1}", e.InnerException.GetType().Name, e.InnerException.Message);
				}

				return 1;
			}
		}
	}
}

