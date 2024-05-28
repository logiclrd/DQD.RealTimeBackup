using System;
using System.IO;
using System.Xml.Serialization;

using Autofac;

using Bytewizer.Backblaze.Client;

using DeltaQ.RTB;
using DeltaQ.RTB.StateCache;
using DeltaQ.RTB.Storage;
using DeltaQ.RTB.Utility;

using DeltaQ.CommandLineParser;

namespace DeltaQ.RTB.Tweaker
{
	class Program
	{
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

			builder.RegisterType<B2RemoteStorage>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<RemoteFileStateCache>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<RemoteFileStateCacheRemoteStorage>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<Timer>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<CacheActionLog>().AsImplementedInterfaces().SingleInstance();

			return builder.Build();
		}

		static int Main()
		{
			var commandLineParser = new CommandLine();

			var arguments = commandLineParser.Parse<CommandLineArguments>();

			if (arguments.ShowUsage || arguments.NoAction)
			{
				commandLineParser.ShowUsage<CommandLineArguments>(Console.Error, "dotnet DeltaQ.RTB.Tweaker.dll", detailed: arguments.ShowUsage);
				return 10;
			}

			var serializer = new XmlSerializer(typeof(OperatingParameters));

			OperatingParameters parameters;

			using (var stream = File.OpenRead("/etc/DeltaQ.RTB.xml"))
				parameters = (OperatingParameters)serializer.Deserialize(stream)!;

			var container = InitializeContainer(parameters);

			var remoteStorage = container.Resolve<IRemoteStorage>();

			remoteStorage.Authenticate();

			if (arguments.UploadEmptyFileToPath != null)
			{
				Console.WriteLine("Creating empty file in remote storage:");
				Console.WriteLine("=> {0}", arguments.UploadEmptyFileToPath);

				remoteStorage.UploadFileDirect(
					arguments.UploadEmptyFileToPath,
					new MemoryStream(),
					System.Threading.CancellationToken.None);
			}

			if (arguments.DeleteGhostStateFiles)
			{
				Console.WriteLine("Enumerating remote file state cache files on the server...");

				foreach (var file in remoteStorage.EnumerateFiles("/state/", false))
				{
					string bareName = Path.GetFileName(file.Path);
					string serverPath = "/state/" + bareName;
					string localPath = "/var/DeltaQ.RTB/FileStateCache/" + bareName;

					Console.Write("- {0}: ", serverPath);

					if (File.Exists(localPath))
						Console.WriteLine("Exists locally, leaving untouched");
					else
					{
						Console.WriteLine("Does not exist locally, sending a deletion to the server");
						remoteStorage.DeleteFileDirect(serverPath, System.Threading.CancellationToken.None);
					}
				}
			}

			if (arguments.CleanUpUnfinishedFiles)
			{
				Console.WriteLine("Enumerating unfinished large files...");

				var minimumUnfinishedFileAge = TimeSpan.FromHours(arguments.MinimumUnfinishedFileAgeHours);

				foreach (var unfinishedFile in remoteStorage.EnumerateUnfinishedFiles())
				{
					Console.WriteLine("- {0}", unfinishedFile.Path);

					var age = DateTime.UtcNow - unfinishedFile.LastModifiedUTC;

					if (age < minimumUnfinishedFileAge)
						Console.WriteLine("    Leaving file alone because it was last modified recently ({0:#0.0} hours ago)", age.TotalHours);
					else
					{
						Console.WriteLine("    Deleting");

						remoteStorage.DeleteFileDirect(unfinishedFile.Path);
					}
				}
			}

			return 0;
		}
	}
}
