using System;
using System.Collections.Generic;
using System.IO;

public class RemoteFileStateCache : IRemoteFileStateCache
{
  // Objectives:
  // 1. Maintain a look-up based on file full paths that report back the file size and MD5 checksum of each file stored on the server
  // 2. Track updates in a sequence of batch files, where the current state is the result of overlaying the batches in order of date
  // 3. Be able to reload the current state from the batch files at any time (e.g. after reboots)
  // 3. Keep batch files synchronized to the server as well
  // 4. Have a mechanism to consolidate batches, merging the oldest batch forward, and updating the server's copy in the same way
  //    => Merge algorithm: if we call the oldest batch 0 and its successor 1, then add all files from 0 that aren't in 1 to 1, and then
  //                        discard 0.
  const int BatchUploadConsolidationDelaySeconds = 30;

  ITimer _timer;
  IRemoteStorage _remoteStorage;
  string _statePath;
  Dictionary<string, FileState> _cache = new Dictionary<string, FileState>();
  List<FileState> _currentBatch = new List<FileState>();
  int _currentBatchNumber;
  StreamWriter? _currentBatchWriter;
  ITimerInstance? _batchUploadTimer;

  public RemoteFileStateCache(ITimer timer, IRemoteStorage remoteStorage, string statePath)
  {
    _timer = timer;
    _remoteStorage = remoteStorage;
    _statePath = statePath;

    LoadCache();
  }

  public FileState? GetFileState(string path)
  {
    _cache.TryGetValue(path, out var state);

    return state;
  }

  public void UpdateFileState(string path, FileState newFileState)
  {
    lock (this)
    {
      _cache[path] = newFileState;

      newFileState.Path = path; // Just in case.

      AppendNewFileStateToCurrentBatch(newFileState);
    }
  }

  int GetBatchCountInCache()
  {
    int count = 0;

    foreach (var batchFile in Directory.EnumerateFiles(_statePath))
      if (int.TryParse(Path.GetFileName(batchFile), out var batchNumber))
        count++;

    return count;
  }

  void LoadCache()
  {
    // Enumerate the batch numbers that are stored locally. Process them in order.
    var batchNumbers = new List<int>();

    foreach (var batchFile in Directory.EnumerateFiles(_statePath))
      if (int.TryParse(Path.GetFileName(batchFile), out var batchNumber))
        batchNumbers.Add(batchNumber);

    batchNumbers.Sort();

    // Load in all saved FileStates. If we encounter one that we already have in the cache,
    // it is a newer state that supersedes the previously-loaded one.
    foreach (var batchNumber in batchNumbers)
    {
      using (var reader = new StreamReader(Path.Combine(_statePath, batchNumber.ToString())))
      {
        var line = reader.ReadLine();

        if (line == null)
          break;

        var fileState = FileState.Parse(line);

        // Overwrite if already present, as the one we just loaded will be newer.
        _cache[fileState.Path] = fileState;
      }
    }
  }

  void AppendNewFileStateToCurrentBatch(FileState newFileState)
  {
    lock (this)
    {
      _currentBatch.Add(newFileState);

      if (_batchUploadTimer == null)
      {
        _batchUploadTimer = _timer.ScheduleAction(TimeSpan.FromSeconds(BatchUploadConsolidationDelaySeconds), BatchUploadTimerElapsed);
      }

      if (_currentBatchWriter == null)
      {
        _currentBatchWriter = new StreamWriter(Path.Combine(_statePath, _currentBatchNumber.ToString()));
        _currentBatchWriter.AutoFlush = true;
      }

      _currentBatchWriter.WriteLine(newFileState);
    }
  }

  void BatchUploadTimerElapsed()
  {
    lock (this)
    {
      _batchUploadTimer?.Dispose();
      _batchUploadTimer = null;

      UploadCurrentBatch();
    }
  }

  void UploadCurrentBatch()
  {
    lock (this)
    {
      string currentBatchPath = Path.Combine(_statePath, _currentBatchNumber.ToString());
      string currentBatchRemotePath = "/state/" + _currentBatchNumber;

      _currentBatchNumber++;
      _currentBatch.Clear();
      _currentBatchWriter?.Close();
      _currentBatchWriter = null;

      using (var stream = File.OpenRead(currentBatchPath))
        _remoteStorage.UploadFile(currentBatchRemotePath, stream);
      
      if (GetBatchCountInCache() > 3)
      {
        int removedBatchNumber = ConsolidateOldestBatch();

        if (removedBatchNumber >= 0)
        {
          string remoteBatchPath = "/state/" + removedBatchNumber;

          _remoteStorage.DeleteFile(remoteBatchPath);
        }
      }
    }
  }

  int ConsolidateOldestBatch()
  {
    lock (this)
    {
      // Find the two oldest batches.
      // If there is only one batch, nothing to do.
      var batchNumbers = new List<int>();

      foreach (var batchFile in Directory.EnumerateFiles(_statePath))
        if (int.TryParse(Path.GetFileName(batchFile), out var batchNumber))
          batchNumbers.Add(batchNumber);

      if (batchNumbers.Count < 2)
        return -1;

      batchNumbers.Sort();

      var oldestBatchNumber = batchNumbers[0];
      var mergeIntoBatchNumber = batchNumbers[1];

      string oldestBatchPath = Path.Combine(_statePath, oldestBatchNumber.ToString());
      string mergeIntoBatchPath = Path.Combine(_statePath, mergeIntoBatchNumber.ToString());

      // Load in the merge-into batch.
      var mergeIntoBatch = new Dictionary<string, FileState>();

      using (var reader = new StreamReader(mergeIntoBatchPath))
      {
        while (true)
        {
          string? line = reader.ReadLine();

          if (line == null)
            break;

          var fileState = FileState.Parse(line);

          mergeIntoBatch[fileState.Path] = fileState;
        }
      }

      // Merge the oldest batch into the merge-into batch. Only entries that aren't superseded in the merge-into
      // batch are used. When the merge-into branch already has a newer FileState, the older one is discarded.
      using (var reader = new StreamReader(oldestBatchPath))
      {
        while (true)
        {
          string? line = reader.ReadLine();

          if (line == null)
            break;

          var fileState = FileState.Parse(line);

          if (!mergeIntoBatch.ContainsKey(fileState.Path))
            mergeIntoBatch[fileState.Path] = fileState;
        }
      }

      // Write out the merged batch. It is written to a ".new" file first, so that the update to the actual
      // batch file path is atomic and cannot possibly contain an incomplete file in the event of an error
      // or power loss or whatnot.
      using (var writer = new StreamWriter(mergeIntoBatchPath + ".new"))
        foreach (var fileState in mergeIntoBatch.Values)
          writer.WriteLine(fileState);

      File.Move(mergeIntoBatchPath + ".new", mergeIntoBatchPath, overwrite: true);
      File.Delete(oldestBatchPath);

      // Return the batch number that no longer exists so that the caller can remove it from remote storage.
      return oldestBatchNumber;
    }
  }
}

