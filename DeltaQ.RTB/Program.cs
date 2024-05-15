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
using DeltaQ.RTB.Interop;
using DeltaQ.RTB.StateCache;
using DeltaQ.RTB.Storage;
using DeltaQ.RTB.Utility;

using Timer = DeltaQ.RTB.Utility.Timer;

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
			if (args.DisableFAN)
				parameters.EnableFileAccessNotify = false;

			return parameters;
		}

		static IContainer InitializeContainer(OperatingParameters parameters)
		{
			var builder = new ContainerBuilder();

			builder.RegisterInstance(parameters);

			builder.RegisterType<BackupAgent>().AsImplementedInterfaces();
			builder.RegisterType<FileAccessNotify>().AsImplementedInterfaces();
			builder.RegisterType<FileSystemMonitor>().AsImplementedInterfaces();
			builder.RegisterType<MD5Checksum>().AsImplementedInterfaces();
			builder.RegisterType<MountTable>().AsImplementedInterfaces();
			builder.RegisterType<OpenByHandleAt>().AsImplementedInterfaces();
			builder.RegisterType<OpenFileHandles>().AsImplementedInterfaces();
			builder.RegisterType<RemoteFileStateCache>().AsImplementedInterfaces();
			builder.RegisterType<RemoteFileStateCacheStorage>().AsImplementedInterfaces();
			builder.RegisterType<B2RemoteStorage>().AsImplementedInterfaces();
			builder.RegisterType<Staging>().AsImplementedInterfaces();
			builder.RegisterType<Timer>().AsImplementedInterfaces();
			builder.RegisterType<ZFS>().AsImplementedInterfaces();

			return builder.Build();
		}

		static int Main()
		{
			CommandLineArguments? args = null;

			try
			{
				args = new CommandLine().Parse<CommandLineArguments>();

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

					backupAgent.Start();

					if (!parameters.Quiet)
						Console.WriteLine("Starting backup agent...");
					backupAgent.Start();

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

				Console.Error.WriteLine(e.Message);

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

