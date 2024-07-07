using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

using CancellationToken = System.Threading.CancellationToken;

using Autofac;

using Bytewizer.Backblaze.Client;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.Utility;

using DQD.CommandLineParser;

namespace DQD.RealTimeBackup.Restore
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

			builder.RegisterInstance(backblazeAgentOptions);

			builder.RegisterType<B2RemoteStorage>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<RemoteFileStateCache>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<RemoteFileStateCacheRemoteStorage>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<ErrorLogger>()
				.UsingConstructor(typeof(OperatingParameters))
				.AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<Timer>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<CacheActionLog>().AsImplementedInterfaces().SingleInstance();

			return builder.Build();
		}

		static int Main()
		{
			var commandLine = new CommandLine();

			var args = commandLine.Parse<CommandLineArguments>();

			if (args.ShowUsage)
			{
				commandLine.ShowUsage<CommandLineArguments>(Console.Error, "dotnet DQD.RealTimeBackup.Restore.dll");
				return 10;
			}

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
						var backblazeClientOptions = container.Resolve<ClientOptions>();

						long savedCutoffSize = backblazeClientOptions.DownloadCutoffSize;

						try
						{
							// For catting to stdout, we must always do a single linear download.
							backblazeClientOptions.DownloadCutoffSize = long.MaxValue;

							string filePath = args.CatFile;

							string contentPointerFilePath = Path.Combine("/content", filePath.TrimStart('/'));

							using (var outputStream = Console.OpenStandardOutput(bufferSize: 1048576))
							{
								storage.DownloadFile(contentPointerFilePath, outputStream, CancellationToken.None);
							}
						}
						finally
						{
							backblazeClientOptions.DownloadCutoffSize = savedCutoffSize;
						}
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

							using (var list = output.BeginList("All Files"))
							{
								foreach (var file in storage.EnumerateFiles("/content/", true))
								{
									// The enumeration removes the supplied prefix from the returned paths, making them
									// relative. But, since we supplied the '/' after 'content', this means that the
									// returned paths are not rooted. Logically, though, they are, so we need to restore
									// this property.

									file.Path = "/" + file.Path;

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
								string directoryPathWithSeparators = directoryPath;

								if (!directoryPathWithSeparators.StartsWith("/"))
									directoryPathWithSeparators = '/' + directoryPathWithSeparators;
								if (!directoryPathWithSeparators.EndsWith("/"))
									directoryPathWithSeparators = directoryPathWithSeparators + '/';

								string contentPointerFilesPrefix = "/content" + directoryPathWithSeparators;

								using (var list = output.BeginList("Directory", directoryPath, args.Recursive))
								{
									foreach (var file in storage.EnumerateFiles(contentPointerFilesPrefix, args.Recursive))
									{
										// The enumeration removes the supplied prefix from the returned paths, making them
										// relative. We need to determine the full path in order to inject file sizes.

										string fullPath = directoryPathWithSeparators + file.Path;

										fileSizeMap.TryGetValue(fullPath, out file.FileSize);
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

							string contentPointerFilePath = Path.Combine("/content", filePath.TrimStart('/'));

							using (var list = output.BeginList("Download", Path.GetDirectoryName(filePath), args.Recursive))
							{
								using (var stream = File.Open(toFilePath, FileMode.Create, FileAccess.ReadWrite))
									storage.DownloadFile(contentPointerFilePath, stream, CancellationToken.None);

								list.EmitFile(toFilePath);
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

							string contentPointerFilesPrefix = Path.Combine("/content", restorePrefix.TrimStart('/'));

							using (var list = output.BeginList("Download", directoryPath, args.Recursive))
							{
								foreach (var file in storage.EnumerateFiles(contentPointerFilesPrefix, args.Recursive))
								{
									string filePath = contentPointerFilesPrefix + file.Path;
									string toFilePath = Path.Combine(toDirectory, file.Path);

									using (var stream = File.Open(toFilePath, FileMode.Create, FileAccess.ReadWrite))
										storage.DownloadFile(filePath, stream, CancellationToken.None);

									list.EmitFile(toFilePath);
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
