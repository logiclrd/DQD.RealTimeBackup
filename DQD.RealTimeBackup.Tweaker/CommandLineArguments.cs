using DQD.CommandLineParser;

namespace DQD.RealTimeBackup.Tweaker
{
	public class CommandLineArguments
	{
		[Switch("/CLEANUPUNFINISHEDFILES", Description =
		  "Deletes unfinished large files. Do not run this while DQD.RealTimeBackup is running, or it " +
			"may delete a file that is being actively uploaded.")]
		public bool CleanUpUnfinishedFiles;

		[Argument("/MINIMUMUNFINISHEDFILEAGEHOURS", Description =
			"Unfinished files that are timestamped more recently than this age will be left alone, " +
			"based on the heuristic that if you have not stopped DQD.RealTimeBackup while running this " +
			"utility, they might actually still be in-progress. This parameter can be set to 0.")]
		public int MinimumUnfinishedFileAgeHours = DefaultMinimumUnfinishedFileAgeHours;

		[Switch("/LISTGHOSTSTATEFILES", Description =
			"Enumerates Remote File State Cache files in remote storage and lists any that no " +
			"longer exist locally.")]
		public bool ListGhostStateFiles;

		[Switch("/DELETEGHOSTSTATEFILES", Description =
			"Enumerates Remote File State Cache files in remote storage and deletes any that no " +
			"longer exist locally.")]
		public bool DeleteGhostStateFiles;

		[Argument("/UPLOADEMPTYFILE", Description =
			"If the Backup Agent is stuck repeatedly trying to delete a specific path which does " +
			"not exist in remote storage, this can be used to place a dummy file there for it to " +
			"delete, unblocking the queue.")]
		public string? UploadEmptyFileToPath;

		[Switch("/LISTFILESWITHMULTIPLEVERSIONS", Description =
			"Scans Remote Storage for files that have been uploaded multiple times. Normally, " +
			"when a file is uploaded, any previous version should be removed. But, Remote Storage " +
			"can hold multiple versions of the exact same path, so if the same path is uploaded " +
			"multiple times, both versions will be stored. Only one of them is meaningful for a " +
			"backup.")]
		public bool ListFilesWithMultipleVersions;

		[Argument("/DOWNLOADALLVERSIONS", Description =
			"Downloads all versions, if any, of the file with the specified path. Specify '*' to " +
			"automatically download all versions of all files identified by /LISTFILESWITHMULTIPLEVERSIONS " +
			"in the same invocation. Files that have differing content in different versions are " +
			"removed from consideration.")]
		public string? DownloadAllVersionsOfFileAtPath;

		[Argument("/DELETEALLBUTONE", Description =
			"If the specified file path has more than one version uploaded, deletes all but one of them. Does " +
			"not verify whether the files are identical. (You can use /DOWNLOADALLVERSIONS for this.) " +
			"Specify '*' to automatically delete all but one version of all files identified by " +
			"/LISTFILESWITHMULTIPLEVERSIONS. Can (and should) be used in conjunction with " +
			"/DOWNLOADALLVERSIONS '*'.")]
		public string? DeleteAllButOneVersionOfFileAtPath;

		[Switch("/REMOVEFILESNOTINCACHE", Description =
			"Automatically deletes versions of files that don't match what's in the Remote File State Cache.")]
		public bool RemoveIncorrectFiles;

		const int DefaultMinimumUnfinishedFileAgeHours = 36;

		[Switch("/?")]
		public bool ShowUsage;

		public bool NoAction =>
			!CleanUpUnfinishedFiles &&
			!ListGhostStateFiles &&
			!DeleteGhostStateFiles &&
			(UploadEmptyFileToPath == null) &&
			!ListFilesWithMultipleVersions &&
			(DownloadAllVersionsOfFileAtPath == null) &&
			(DeleteAllButOneVersionOfFileAtPath == null) &&
			!RemoveIncorrectFiles;
	}
}
