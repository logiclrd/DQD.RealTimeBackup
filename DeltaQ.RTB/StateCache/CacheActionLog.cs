using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace DeltaQ.RTB.StateCache
{
	public class CacheActionLog : ICacheActionLog
	{
		const string ActionQueueDirectoryName = "ActionQueue";

		OperatingParameters _parameters;

		public CacheActionLog(OperatingParameters parameters)
		{
			_parameters = parameters;
			_actionQueuePath = Path.Combine(_parameters.RemoteFileStateCachePath, ActionQueueDirectoryName);
		}

		string _actionQueuePath;

		public string ActionQueuePath
		{
			get { return _actionQueuePath; }
			[EditorBrowsable(EditorBrowsableState.Never)] internal set { _actionQueuePath = value; }
		}

		public void EnsureDirectoryExists()
		{
			Directory.CreateDirectory(ActionQueuePath);
		}

		public IEnumerable<long> EnumerateActionKeys()
		{
			foreach (string filePath in Directory.GetFiles(ActionQueuePath))
				if (long.TryParse(Path.GetFileName(filePath), out var actionKey))
					yield return actionKey;
		}

		public string GetQueueActionFileName(long key)
		{
			return Path.Combine(_actionQueuePath, key.ToString());
		}

		public void LogAction(CacheAction action)
		{
			long actionKey = DateTime.UtcNow.Ticks;

			string actionFileName = GetQueueActionFileName(actionKey);

			while (true)
			{
				if (!File.Exists(actionFileName))
					break;

				actionKey++;
				actionFileName = GetQueueActionFileName(actionKey);
			}

			action.ActionFileName = actionFileName;

			File.WriteAllText(action.ActionFileName, action.Serialize());
		}

		public CacheAction RehydrateAction(long key)
		{
			string path = GetQueueActionFileName(key);

			string serialized = File.ReadAllText(path);

			var action = CacheAction.Deserialize(serialized);

			action.ActionFileName = path;

			return action;
		}

		public void ReleaseAction(CacheAction action)
		{
			if (action.ActionFileName != null)
			{
				File.Delete(action.ActionFileName);
				action.ActionFileName = null;
			}
		}
	}
}
