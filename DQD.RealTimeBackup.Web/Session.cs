using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using DQD.RealTimeBackup.StateCache;

namespace DQD.RealTimeBackup.Web
{
	public class Session
	{
		public string SessionID;
		public DateTime LastActivityDateTimeUTC;

		public Task? LoadFileStateTask;
		public double LoadFileStateProgress;

		object _sync = new object();
		List<FileState> _allFiles = new List<FileState>();
		Dictionary<string, string> _contentKeyByFilePath = new Dictionary<string, string>();

		public Session(string sessionID)
		{
			this.SessionID = sessionID;
		}

		public void BeginLoadFileState(Func<IRemoteFileStateCache> remoteFileStateCacheFactory)
		{
			if ((LoadFileStateTask != null)
			 && (LoadFileStateTask.Status < TaskStatus.RanToCompletion))
				return;

			LoadFileStateTask = Task.Run(
				() =>
				{
					var remoteFileStateCache = remoteFileStateCacheFactory();

					LoadFileState(remoteFileStateCache);
				});
		}

		public void LoadFileState(IRemoteFileStateCache remoteFileStateCache)
		{
			lock (_sync)
			{
				_allFiles.Clear();
				_contentKeyByFilePath.Clear();
			}

			var buffer = new List<FileState>();

			Action<double> progressCallback =
				progress =>
				{
					LoadFileStateProgress = progress;
				};

			foreach (var fileState in remoteFileStateCache.EnumerateFileStates(progressCallback))
			{
				buffer.Add(fileState);

				if (buffer.Count >= 128)
				{
					lock (_sync)
					{
						foreach (var bufferedFileState in buffer)
						{
							_allFiles.Add(bufferedFileState);
							_contentKeyByFilePath[bufferedFileState.Path] = bufferedFileState.ContentKey;
						}
					}

					buffer.Clear();
				}
			}

			if (buffer.Count > 0)
			{
				lock (_sync)
				{
					foreach (var fileState in buffer)
					{
						_allFiles.Add(fileState);
						_contentKeyByFilePath[fileState.Path] = fileState.ContentKey;
					}

					_allFiles.Sort((a, b) => a.Path.CompareTo(b.Path));
				}
			}
		}

		static char[] PathSeparatorCharacters = [ Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar ];

		public int GetFileCount() => _allFiles.Count;

		public IEnumerable<FileInformation> GetFilesInDirectory(string directoryPath, bool recursive)
		{
			if (!directoryPath.EndsWith("/"))
				directoryPath += "/";

			int firstMatchIndex = FindIndexOfFirstPathWithPrefix(directoryPath);

			if (firstMatchIndex >= 0)
			{
				int index = firstMatchIndex;

				var candidate = _allFiles[index];

				while (candidate.Path.StartsWith(directoryPath))
				{
					bool include = true;

					if (!recursive)
					{
						if (candidate.Path.IndexOfAny(PathSeparatorCharacters, startIndex: directoryPath.Length) > 0)
							include = false;
					}

					if (include)
					{
						var result = new FileInformation();

						result.Path = candidate.Path;
						result.FileSize = candidate.FileSize;
						result.LastModifiedUTC = candidate.LastModifiedUTC;
						result.FileIndex = index;

						yield return result;
					}

					index++;

					if (index >= _allFiles.Count)
						break;

					candidate = _allFiles[index];
				}
			}
		}

		public IEnumerable<string> GetDirectoriesInDirectory(string directoryPath, bool recursive)
		{
			if (!directoryPath.EndsWith("/"))
				directoryPath += "/";

			int firstMatchIndex = FindIndexOfFirstPathWithPrefix(directoryPath);

			if (firstMatchIndex >= 0)
			{
				int index = firstMatchIndex;

				var matches = new HashSet<string>();

				var candidate = _allFiles[index];

				while (candidate.Path.StartsWith(directoryPath))
				{
					int separatorIndex = candidate.Path.IndexOfAny(PathSeparatorCharacters, startIndex: directoryPath.Length);

					while (separatorIndex >= 0)
					{
						string containerPath = candidate.Path.Substring(0, separatorIndex);

						if (matches.Add(containerPath))
							yield return containerPath;

						if (!recursive)
							break;
						
						separatorIndex = candidate.Path.IndexOfAny(PathSeparatorCharacters, startIndex: separatorIndex + 1);
					}

					index++;

					if (index >= _allFiles.Count)
						break;

					candidate = _allFiles[index];
				}
			}
		}

		int FindIndexOfFirstPathWithPrefix(string prefix)
		{
			if (_allFiles.Count == 0)
				return -1;

			if (prefix == "/")
				return 0;

			int firstCandidateIndex = 0;
			int lastCandidateIndex = _allFiles.Count - 1;

			while (true)
			{
				if (firstCandidateIndex == lastCandidateIndex)
				{
					if (_allFiles[firstCandidateIndex].Path.StartsWith(prefix))
						return firstCandidateIndex;
					else 	
						return -1;
				}

				int probeIndex = (firstCandidateIndex + lastCandidateIndex) >> 1;

				if (string.Compare(_allFiles[probeIndex].Path, prefix) < 0)
					firstCandidateIndex = probeIndex + 1;
				else
					lastCandidateIndex = probeIndex; // The probed path could be a match, do not exclude from range.
			}
		}

		public FileState GetFileByIndex(int fileIndex)
		{
			return _allFiles[fileIndex];
		}
	}
}
