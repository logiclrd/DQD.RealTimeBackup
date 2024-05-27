using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using Autofac;

using Bytewizer.Backblaze.Client;

using DeltaQ.RTB.StateCache;
using DeltaQ.RTB.Storage;
using DeltaQ.RTB.Utility;

using DeltaQ.CommandLineParser;

namespace DeltaQ.RTB.Restore
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
			var args = new CommandLine().Parse<CommandLineArguments>();

			IOutput? output;

			if (args.CatFile != null)
				output = null;
			else if (args.XML)
				output = new XMLOutput();
			else
				output = new TextOutput();

			using (output)
			{
				try
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
							Console.Error.WriteLine("Unable to parse configuration file: {0}", args.ConfigurationPath);
							Console.Error.WriteLine(e);

							return 1;
						}
					}

					var container = InitializeContainer(parameters);

					var remoteFileStateCache = container.Resolve<IRemoteFileStateCache>();
					var storage = container.Resolve<IRemoteStorage>();

					storage.Authenticate();

					if (args.CatFile != null)
					{
						string filePath = args.CatFile;

						string toFilePath = Path.Combine(
							args.RestoreTo == null ? "." : args.RestoreTo,
							filePath.TrimStart('/'));

						string toDirectory = Path.GetDirectoryName(toFilePath)!;

						if (toDirectory != ".")
							Directory.CreateDirectory(toDirectory);

						using (var outputStream = Console.OpenStandardOutput(bufferSize: 1048576))
							storage.DownloadFile(filePath, outputStream);
					}

					if (output == null)
						return 0;

					var fileSizeMapBuilder = new Lazy<Dictionary<string, long>>(
						() =>
						{
							return remoteFileStateCache.EnumerateFileStates().ToDictionary(
								keySelector: fileState => fileState.Path,
								elementSelector: fileState => fileState.FileSize);
						});

					using (output)
					{
						if (args.ListAllFiles)
						{
							var fileSizeMap = fileSizeMapBuilder.Value;

							using (var list = output.BeginList("AllFiles"))
							{
								foreach (var file in storage.EnumerateFiles("/", true))
								{
									fileSizeMap.TryGetValue(file.Path, out file.FileSize);
									list.EmitFile(file);
								}
							}
						}

						if (args.ListDirectory.Any())
						{
							var fileSizeMap = fileSizeMapBuilder.Value;

							foreach (var directoryPath in args.ListDirectory)
							{
								using (var list = output.BeginList("Directory", directoryPath, args.Recursive))
								{
									foreach (var file in storage.EnumerateFiles(directoryPath, args.Recursive))
									{
										fileSizeMap.TryGetValue(file.Path, out file.FileSize);
										list.EmitFile(file);
									}
								}
							}
						}

						foreach (var filePath in args.RestoreFile)
						{
							string toFilePath = Path.Combine(
								args.RestoreTo == null ? "." : args.RestoreTo,
								filePath.TrimStart('/'));

							string toDirectory = Path.GetDirectoryName(toFilePath)!;

							if (toDirectory != ".")
								Directory.CreateDirectory(toDirectory);

							using (var list = output.BeginList("Download", Path.GetDirectoryName(filePath), args.Recursive))
							{
								using (var stream = File.OpenWrite(toFilePath))
									storage.DownloadFile(filePath, stream);

								list.EmitFile(filePath);
							}
						}

						foreach (var directoryPath in args.RestoreDirectory)
						{
							string restorePrefix = directoryPath.TrimEnd('/') + '/';

							string toDirectory = Path.Combine(
								args.RestoreTo == null ? "." : args.RestoreTo,
								directoryPath.TrimStart('/'));

							if (toDirectory != ".")
								Directory.CreateDirectory(toDirectory);

							using (var list = output.BeginList("Download", directoryPath, args.Recursive))
							{
								foreach (var file in storage.EnumerateFiles(directoryPath, args.Recursive))
								{
									string filePath = Path.Combine(directoryPath, file.Path);
									string toFilePath = Path.Combine(toDirectory, file.Path);

									using (var stream = File.OpenWrite(toFilePath))
										storage.DownloadFile(filePath, stream);

									list.EmitFile(file);
								}
							}
						}
					}

					return 0;
				}
				catch (Exception e)
				{
					output?.EmitError(e);

					return 1;
				}
			}
		}
	}
}
