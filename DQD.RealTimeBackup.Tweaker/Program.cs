using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

using CancellationToken = System.Threading.CancellationToken;

using Autofac;

using Bytewizer.Backblaze.Client;

using DQD.RealTimeBackup.Bridge.Notifications;
using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.Utility;

using DQD.CommandLineParser;

namespace DQD.RealTimeBackup.Tweaker
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

			builder.RegisterType<ErrorLogger>()
				.UsingConstructor(typeof(OperatingParameters))
				.AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<NotificationBus>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<B2RemoteStorage>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<RemoteFileStateCache>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<RemoteFileStateCacheRemoteStorage>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<Timer>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<CacheActionLog>().AsImplementedInterfaces().SingleInstance();
			builder.RegisterType<ContentKeyGenerator>().AsImplementedInterfaces().SingleInstance();

			return builder.Build();
		}

		static int Main()
		{
			var commandLineParser = new CommandLine();

			var arguments = commandLineParser.Parse<CommandLineArguments>();

			if (arguments.ShowUsage || arguments.NoAction)
			{
				commandLineParser.ShowUsage<CommandLineArguments>(Console.Error, "dotnet DQD.RealTimeBackup.Tweaker.dll", detailed: arguments.ShowUsage);
				return 10;
			}

			var serializer = new XmlSerializer(typeof(OperatingParameters));

			OperatingParameters parameters;

			using (var stream = File.OpenRead("/etc/DQD.RealTimeBackup.xml"))
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

			if (arguments.ListGhostStateFiles || arguments.DeleteGhostStateFiles)
			{
				Console.WriteLine("Enumerating remote file state cache files on the server...");

				foreach (var file in remoteStorage.EnumerateFiles("/state/", false))
				{
					string bareName = Path.GetFileName(file.Path);
					string serverPath = "/state/" + bareName;
					string localPath = "/var/DQD.RealTimeBackup/FileStateCache/" + bareName;

					Console.Write("- {0}: ", serverPath);

					if (File.Exists(localPath))
						Console.WriteLine("Exists locally, leaving untouched");
					else
					{
						if (arguments.DeleteGhostStateFiles)
						{
							Console.WriteLine("Does not exist locally, sending a deletion to the server");
							remoteStorage.DeleteFileDirect(serverPath, System.Threading.CancellationToken.None);
						}
						else
							Console.WriteLine("File does not exist locally and should have been deleted from remote storage.");
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
						Console.WriteLine("    Cancelling large file");

						remoteStorage.CancelUnfinishedFile(unfinishedFile.Path, CancellationToken.None);
					}
				}
			}

			List<string>? filesWithMultipleVersions = null;

			if (arguments.ListFilesWithMultipleVersions)
			{
				if (arguments.DownloadAllVersionsOfFileAtPath == "*")
					filesWithMultipleVersions = new List<string>();

				Console.WriteLine("Enumerating files...");

				var paths = new HashSet<string>();
				var duplicatePaths = new HashSet<string>();

				foreach (var file in remoteStorage.EnumerateFiles("/content/", recursive: true))
				{
					string filePath = file.Path;

					if (!filePath.StartsWith("/"))
						filePath = '/' + filePath;

					if (!paths.Add(filePath))
					{
						if (duplicatePaths.Add(filePath))
						{
							if (duplicatePaths.Count == 1)
								Console.WriteLine("Files with multiple versions in remote storage:");

							Console.WriteLine("- {0}", filePath);

							filesWithMultipleVersions?.Add(filePath);
						}
					}

					if ((paths.Count & 1023) == 0)
					{
						Console.Write(paths.Count);

						if (Console.IsOutputRedirected)
							Console.WriteLine();
						else
							Console.CursorLeft = 0;
					}
				}
			}

			if (arguments.DownloadAllVersionsOfFileAtPath != null)
			{
				bool inputRedirected = (arguments.DownloadAllVersionsOfFileAtPath == "*");

				if (!inputRedirected)
					filesWithMultipleVersions = new List<string>() { arguments.DownloadAllVersionsOfFileAtPath };

				var fileVersionsData = inputRedirected ? new List<byte[]>() : default;

				for (int i=0; i < filesWithMultipleVersions!.Count; i++)
				{
					string filePath = "/" + filesWithMultipleVersions[i].TrimStart('/');

					Console.WriteLine("Scanning for file versions of: {0}", filePath);

					string searchPrefix = "/content" + Path.GetDirectoryName(filePath);
					string searchTarget = "/content" + filePath;

					fileVersionsData?.Clear();

					foreach (var file in remoteStorage.EnumerateFiles(searchPrefix, recursive: false))
					{
						string fullPath = Path.Combine(searchPrefix, file.Path);

						if (fullPath == searchTarget)
						{
							if (!inputRedirected)
							{
								Console.WriteLine("File ID: {0}", file.RemoteFileID);
								Console.WriteLine("Last Modified (UTC): {0}", file.LastModifiedUTC);
								Console.WriteLine("File Size: {0}", file.FileSize);

								Console.WriteLine();

								if (file.FileSize > 100000)
									continue;

								Console.WriteLine("---");
							}

							var buffer = new MemoryStream();

							remoteStorage.DownloadFileByID(file.RemoteFileID, buffer, CancellationToken.None);

							if (!inputRedirected)
							{
								Console.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
								Console.WriteLine("---");
							}

							fileVersionsData?.Add(buffer.ToArray());
						}
					}

					if ((fileVersionsData != null) && fileVersionsData.Any())
					{
						if (!ByteArraysAreIdentical(fileVersionsData))
						{
							Console.WriteLine("SKIPPING: {0}", filePath);
							Console.WriteLine("Files do not have identical content");

							filesWithMultipleVersions.RemoveAt(i);
							i--;
						}
					}
				}
			}

			if (arguments.DeleteAllButOneVersionOfFileAtPath != null)
			{
				bool inputRedirected = (arguments.DownloadAllVersionsOfFileAtPath == "*");

				if (!inputRedirected || (filesWithMultipleVersions == null))
				{
					filesWithMultipleVersions = new List<string>();

					if (arguments.DownloadAllVersionsOfFileAtPath != null)
						filesWithMultipleVersions.Add(arguments.DownloadAllVersionsOfFileAtPath);
				}

				for (int i=0; i < filesWithMultipleVersions.Count; i++)
				{
					string filePath = "/" + filesWithMultipleVersions[i].TrimStart('/');

					Console.WriteLine("Scanning for file versions of: {0}", filePath);

					string searchPrefix = "/content" + Path.GetDirectoryName(filePath);
					string searchTarget = "/content" + filePath;

					bool foundFirst = false;

					foreach (var file in remoteStorage.EnumerateFiles(searchPrefix, recursive: false))
					{
						string fullPath = Path.Combine(searchPrefix, file.Path);

						if (fullPath == searchTarget)
						{
							if (!foundFirst)
							{
								Console.WriteLine("Keeping File ID: {0}", file.RemoteFileID);
								foundFirst = true;
							}
							else
							{
								Console.WriteLine("Deleting File ID: {0}", file.RemoteFileID);
								remoteStorage.DeleteFileByID(file.RemoteFileID, fullPath, CancellationToken.None);
							}
						}
					}
				}
			}

			if (arguments.RemoveIncorrectFiles)
			{
				Console.WriteLine("Enumerating files...");

				var fileInfoByContentKey = new Dictionary<string, RemoteFileInfo>();
				var pathByContentKey = new Dictionary<string, string>();

				foreach (var file in remoteStorage.EnumerateFiles("", recursive: true))
				{
					string filePath = file.Path;

					if (filePath.StartsWith("/state/"))
						continue;

					if (filePath.StartsWith("/content/"))
					{
						var buffer = new MemoryStream();

						remoteStorage.DownloadFileDirect(filePath, buffer, CancellationToken.None);

						string contentKey = Encoding.UTF8.GetString(buffer.ToArray());

						pathByContentKey[contentKey] = filePath;
					}
					else
						fileInfoByContentKey[filePath] = file;

					if ((pathByContentKey.Count & 31) == 0)
					{
						Console.Write(pathByContentKey.Count + fileInfoByContentKey.Count);

						if (Console.IsOutputRedirected)
							Console.WriteLine();
						else
							Console.CursorLeft = 0;
					}
				}

				Console.WriteLine("Enumerated {0} files", pathByContentKey.Count + fileInfoByContentKey.Count);
				Console.WriteLine("Loading the Remote File State Cache...");

				var remoteFileStateCache = container.Resolve<IRemoteFileStateCache>();

				remoteFileStateCache.LoadCache();

				foreach (var pathMapping in pathByContentKey)
				{
					string contentKey = pathMapping.Key;
					string path = pathMapping.Value;

					if (!fileInfoByContentKey.TryGetValue(contentKey, out var fileInfo))
					{
						Console.WriteLine("File pointer: {0}", path);
						Console.WriteLine("... points at content key: {0}", contentKey);
						Console.WriteLine("... but no content by that content key exists.");
					}
					else
					{
						var fileState = remoteFileStateCache.GetFileState(path);

						if (fileState == null)
						{
							Console.WriteLine("File pointer: {0}", path);
							Console.WriteLine("... is not in the Remote File State Cache");
						}
						else if (fileInfo.FileSize != fileState.FileSize)
						{
							Console.WriteLine("File pointer: {0}", path);
							Console.WriteLine("... points at content with size {0:#,##0}", fileInfo.FileSize);
							Console.WriteLine("... but the Remote File State Cache expects size {0:#,##0}", fileState.FileSize);
						}
					}
				}
			}

			return 0;
		}

		static bool ByteArraysAreIdentical(IEnumerable<byte[]> listOfArrays)
		{
			var arrays = listOfArrays.ToArray();

			for (int i = 1; i < arrays.Length; i++)
				if (arrays[i].Length != arrays[0].Length)
					return false;

			for (int o = 0; o < arrays[0].Length; o++)
				for (int i = 1; i < arrays.Length; i++)
					if (arrays[i][o] != arrays[0][o])
						return false;

			return true;
		}
	}
}
