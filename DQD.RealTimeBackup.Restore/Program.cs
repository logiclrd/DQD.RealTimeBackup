using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

using CancellationToken = System.Threading.CancellationToken;

using Autofac;

using Bytewizer.Backblaze.Client;

using DQD.Backblaze.Agent.Autofac;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.Utility;

using DQD.CommandLineParser;

namespace DQD.RealTimeBackup.Restore
{
	class Program
	{
		static IContainer InitializeContainer(CommandLineArguments args, OperatingParameters parameters)
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

			if (args.UseFileState)
				builder.RegisterType<RemoteFileStateCacheStorage>().AsImplementedInterfaces().SingleInstance();
			else
				builder.RegisterType<RemoteFileStateCacheRemoteStorage>().AsImplementedInterfaces().SingleInstance();

			builder.RegisterType<ErrorLogger>()
				.UsingConstructor(typeof(OperatingParameters))
				.AsImplementedInterfaces().SingleInstance();

			builder.RegisterType<Timer>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<CacheActionLog>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<ContentKeyGenerator>().AsImplementedInterfaces().SingleInstance();

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
				output = new XMLOutput(trustFileSizes: args.UseFileState);
			else
				output = new TextOutput(trustFileSizes: args.UseFileState);

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

					var container = InitializeContainer(args, parameters);

					var remoteFileStateCache = container.Resolve<IRemoteFileStateCache>();
					var storage = container.Resolve<IRemoteStorage>();

					bool storageAuthenticated = false;

					void EnsureStorageAuthenticated()
					{
						if (!storageAuthenticated)
						{
							storageAuthenticated = true;
							storage.Authenticate();
						}
					}

					var fileStatesMapBuilder = new Lazy<Dictionary<string, FileState>>(
						() =>
						{
							if (!args.UseFileState)
								EnsureStorageAuthenticated();

							return remoteFileStateCache.EnumerateFileStates().ToDictionary(
								keySelector: fileState => fileState.Path,
								elementSelector: fileState => fileState);
						});

					if (args.CatFile != null)
					{
						var backblazeClientOptions = container.Resolve<ClientOptions>();

						long savedCutoffSize = backblazeClientOptions.DownloadCutoffSize;

						try
						{
							// For catting to stdout, we must always do a single linear download.
							backblazeClientOptions.DownloadCutoffSize = long.MaxValue;

							string filePath = args.CatFile;

							FileState? fileState = null;

							if (args.UseFileState)
							{
								var fileStatesMap = fileStatesMapBuilder.Value;

								fileStatesMap.TryGetValue(filePath, out fileState);
							}

							EnsureStorageAuthenticated();

							if (!args.UseFileState || (fileState == null))
							{
								string contentPointerFilePath = Path.Combine("/content", filePath.TrimStart('/'));

								using (var outputStream = Console.OpenStandardOutput(bufferSize: 1048576))
								{
									storage.DownloadFile(contentPointerFilePath, outputStream, CancellationToken.None);
								}
							}
							else
							{
								using (var outputStream = Console.OpenStandardOutput(bufferSize: 1048576))
								{
									if (!fileState.IsInParts)
										storage.DownloadFileDirect(fileState.ContentKey, outputStream, CancellationToken.None);
									else
									{
										int filePartCount = (int)((fileState.FileSize + parameters.FilePartSize - 1) / parameters.FilePartSize);

										for (int partNumber=1; partNumber <= filePartCount; partNumber++)
											storage.DownloadFilePartDirect(fileState.ContentKey, partNumber, outputStream, CancellationToken.None);
									}
								}
							}
						}
						finally
						{
							backblazeClientOptions.DownloadCutoffSize = savedCutoffSize;
						}
					}

					if (output == null)
						return 0;

					using (output)
					{
						if (args.ListAllFiles)
						{
							var fileStatesMap = fileStatesMapBuilder.Value;

							using (var list = output.BeginList("All Files"))
							{
								if (args.UseFileState)
								{
									var fileNamesSorted = fileStatesMap.Keys.ToList();

									fileNamesSorted.Sort();

									foreach (var path in fileNamesSorted)
									{
										var fileState = fileStatesMap[path];

										var file = new RemoteFileInfo(
											fileState.Path,
											fileState.FileSize,
											fileState.LastModifiedUTC,
											"");

										list.EmitFile(file);
									}
								}
								else
								{
									EnsureStorageAuthenticated();

									foreach (var file in storage.EnumerateFiles("/content/", true))
									{
										// The enumeration removes the supplied prefix from the returned paths, making them
										// relative. But, since we supplied the '/' after 'content', this means that the
										// returned paths are not rooted. Logically, though, they are, so we need to restore
										// this property.

										file.Path = "/" + file.Path;

										if (fileStatesMap.TryGetValue(file.Path, out var fileState))
											file.FileSize = fileState.FileSize;

										list.EmitFile(file);
									}
								}
							}
						}

						if (args.ListDirectory.Any())
						{
							var fileStatesMap = fileStatesMapBuilder.Value;

							var fileNamesSorted = new List<string>();

							if (args.UseFileState)
							{
								fileNamesSorted.AddRange(fileStatesMap.Keys);
								fileNamesSorted.Sort();
							}

							foreach (var directoryPath in args.ListDirectory)
							{
								string directoryPathWithSeparators = directoryPath;

								if (!directoryPathWithSeparators.StartsWith("/"))
									directoryPathWithSeparators = '/' + directoryPathWithSeparators;
								if (!directoryPathWithSeparators.EndsWith("/"))
									directoryPathWithSeparators = directoryPathWithSeparators + '/';

								if (args.UseFileState)
								{
									using (var list = output.BeginList("Directory", directoryPath, args.Recursive))
									{
										for (int i=0; i < fileNamesSorted.Count; i++)
										{
											if (fileNamesSorted[i].StartsWith(directoryPathWithSeparators))
											{
												// The enumeration removes the supplied prefix from the returned paths, making them
												// relative. We need to determine the full path in order to inject file sizes.

												string fullPath = fileNamesSorted[i];

												if (!args.Recursive)
												{
													if (fullPath.IndexOf('/', directoryPathWithSeparators.Length) > 0)
														continue;
												}

												string relativePath = fullPath.Substring(directoryPathWithSeparators.Length);

												var fileState = fileStatesMap[fullPath];

												var file = new RemoteFileInfo(
													relativePath,
													fileState.FileSize,
													fileState.LastModifiedUTC,
													"");

												list.EmitFile(file);
											}
											else if (string.Compare(fileNamesSorted[i], directoryPathWithSeparators) > 0)
												break;
										}
									}
								}
								else
								{
									EnsureStorageAuthenticated();

									string contentPointerFilesPrefix = "/content" + directoryPathWithSeparators;

									using (var list = output.BeginList("Directory", directoryPath, args.Recursive))
									{
										foreach (var file in storage.EnumerateFiles(contentPointerFilesPrefix, args.Recursive))
										{
											// The enumeration removes the supplied prefix from the returned paths, making them
											// relative. We need to determine the full path in order to inject file sizes.

											string fullPath = directoryPathWithSeparators + file.Path;

											if (fileStatesMap.TryGetValue(fullPath, out var fileState))
												file.FileSize = fileState.FileSize;

											list.EmitFile(file);
										}
									}
								}
							}
						}

						if (args.RestoreFile.Any())
						{
							EnsureStorageAuthenticated();

							var fileStatesMap = fileStatesMapBuilder.Value;

							foreach (var filePath in args.RestoreFile)
							{
								string toFilePath = Path.Combine(
									args.RestoreTo == null ? "." : args.RestoreTo,
									filePath.TrimStart('/'));

								string toDirectory = Path.GetDirectoryName(toFilePath)!;

								if (toDirectory != ".")
									Directory.CreateDirectory(toDirectory);

								using (var list = output.BeginList("Download", Path.GetDirectoryName(filePath), args.Recursive, trustFileSizes: true))
								{
									if (args.UseFileState && fileStatesMap.TryGetValue(filePath, out var fileState))
									{
										using (var stream = File.Open(toFilePath, FileMode.Create, FileAccess.ReadWrite))
										{
											if (!fileState.IsInParts)
												storage.DownloadFileDirect(fileState.ContentKey, stream, CancellationToken.None);
											else
											{
												int filePartCount = (int)((fileState.FileSize + parameters.FilePartSize - 1) / parameters.FilePartSize);

												for (int partNumber=1; partNumber <= filePartCount; partNumber++)
													storage.DownloadFilePartDirect(fileState.ContentKey, partNumber, stream, CancellationToken.None);
											}
										}
									}
									else
									{
										string contentPointerFilePath = Path.Combine("/content", filePath.TrimStart('/'));

										using (var stream = File.Open(toFilePath, FileMode.Create, FileAccess.ReadWrite))
											storage.DownloadFile(contentPointerFilePath, stream, CancellationToken.None);
									}

									list.EmitFile(toFilePath);
								}
							}
						}

						if (args.RestoreDirectory.Any())
						{
							EnsureStorageAuthenticated();

							var fileStatesMap = args.UseFileState ? fileStatesMapBuilder.Value : default;

							var fileNamesSorted = fileStatesMap?.Keys?.ToList();

							fileNamesSorted?.Sort();

							foreach (var directoryPath in args.RestoreDirectory)
							{
								string restorePrefix = directoryPath.TrimEnd('/') + '/';

								string toDirectory = Path.Combine(
									args.RestoreTo == null ? "." : args.RestoreTo,
									directoryPath.TrimStart('/'));

								if (toDirectory != ".")
									Directory.CreateDirectory(toDirectory);

								if (args.UseFileState)
								{
									using (var list = output.BeginList("Download", directoryPath, args.Recursive))
									{
										for (int i=0; i < fileNamesSorted!.Count; i++)
										{
											if (fileNamesSorted[i].StartsWith(restorePrefix))
											{
												string filePath = fileNamesSorted[i];
												string relativePath = filePath.Substring(restorePrefix.Length);

												if (relativePath.IndexOf('/') > 0)
												{
													if (args.Recursive)
														Directory.CreateDirectory(Path.Combine(toDirectory, Path.GetDirectoryName(relativePath)!));
													else
														continue;
												}

												var fileState = fileStatesMap![filePath];

												string toFilePath = Path.Combine(toDirectory, relativePath.TrimStart('/'));

												using (var stream = File.Open(toFilePath, FileMode.Create, FileAccess.ReadWrite))
												{
													if (!fileState.IsInParts)
														storage.DownloadFileDirect(fileState.ContentKey, stream, CancellationToken.None);
													else
													{
														int filePartCount = (int)((fileState.FileSize + parameters.FilePartSize - 1) / parameters.FilePartSize);

														for (int partNumber=1; partNumber <= filePartCount; partNumber++)
															storage.DownloadFilePartDirect(fileState.ContentKey, partNumber, stream, CancellationToken.None);
													}
												}

												list.EmitFile(toFilePath);
											}
											else if (string.Compare(fileNamesSorted[i], restorePrefix) > 0)
												break;
										}
									}
								}
								else
								{
									string contentPointerFilesPrefix = Path.Combine("/content", restorePrefix.TrimStart('/'));

									using (var list = output.BeginList("Download", directoryPath, args.Recursive, trustFileSizes: true))
									{
										foreach (var file in storage.EnumerateFiles(contentPointerFilesPrefix, args.Recursive))
										{
											string filePath = contentPointerFilesPrefix + file.Path;
											string toFilePath = Path.Combine(toDirectory, file.Path);

											Directory.CreateDirectory(Path.GetDirectoryName(toFilePath)!);

											using (var stream = File.Open(toFilePath, FileMode.Create, FileAccess.ReadWrite))
												storage.DownloadFile(filePath, stream, CancellationToken.None);

											list.EmitFile(toFilePath);
										}
									}
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
