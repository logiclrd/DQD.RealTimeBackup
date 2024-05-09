using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

public class BackupAgent : IBackupAgent
{
  // - get events from FileSystemMonitor
  // - check for open file handles with OpenFileHandles
  // - find or create a ZFS snapshot
  // - copy file to staging area (if not too large) & enqueue
  // - release ZFS snapshot

  OperatingParameters _parameters;

  bool _stopping = false;
  
  ITimer _timer;
  IFileSystemMonitor _monitor;
  IOpenFileHandles _openFileHandles;
  IZFS _zfs;
  IStaging _staging;
  IRemoteFileStateCache _remoteFileStateCache;
  IRemoteStorage _storage;

  public BackupAgent(OperatingParameters parameters, ITimer timer, IFileSystemMonitor monitor, IOpenFileHandles openFileHandles, IZFS zfs, IStaging staging, IRemoteFileStateCache remoteFileStateCache, IRemoteStorage storage)
  {
    _parameters = parameters;

    _timer = timer;
    _monitor = monitor;
    _openFileHandles = openFileHandles;
    _zfs = zfs;
    _staging = staging;
    _remoteFileStateCache = remoteFileStateCache;
    _storage = storage;

    _monitor.PathUpdate += monitor_PathUpdate;
    _monitor.PathMove += monitor_PathMove;
  }

  void monitor_PathUpdate(object? sender, PathUpdate update)
  {
    BeginQueuePathForOpenFilesCheck(update.Path);
  }

  void monitor_PathMove(object? sender, PathMove move)
  {
    // TODO: Consolidate two events per file: move from & move to
  }

  object _snapshotSharingDelaySync = new object();
  ITimerInstance? _snapshotSharingDelay;
  List<string> _snapshotSharingBatch = new List<string>();

  void BeginQueuePathForOpenFilesCheck(string path)
  {
    lock (_snapshotSharingDelaySync)
    {
      if (_snapshotSharingDelay == null)
        _snapshotSharingDelay = _timer.ScheduleAction(_parameters.SnapshotSharingWindow, EndQueuePathForOpenFilesCheck);

      _snapshotSharingBatch.Add(path);
    }
  }

  void EndQueuePathForOpenFilesCheck()
  {
    lock (_snapshotSharingDelaySync)
    {
      _snapshotSharingDelay?.Dispose();
      _snapshotSharingDelay = null;

      var snapshot = _zfs.CreateSnapshot("RTB-" + DateTime.UtcNow.Ticks);

      var snapshotReferenceTracker = new SnapshotReferenceTracker(snapshot);

      foreach (string path in _snapshotSharingBatch)
        QueuePathForOpenFilesCheck(snapshotReferenceTracker.AddReference(path));

      _snapshotSharingBatch.Clear();
    }
  }

  class SnapshotReferenceWithTimeout
  {
    public SnapshotReference SnapshotReference;
    public DateTime TimeoutUTC;

    public SnapshotReferenceWithTimeout(SnapshotReference snapshotReference)
    {
      this.SnapshotReference = snapshotReference;
    }
  }

  object _openFilesSync = new object();
  List<SnapshotReferenceWithTimeout> _openFiles = new List<SnapshotReferenceWithTimeout>();

  void QueuePathForOpenFilesCheck(SnapshotReference reference)
  {
    lock (_openFilesSync)
    {
      var withTimeout = new SnapshotReferenceWithTimeout(reference);

      withTimeout.TimeoutUTC = DateTime.UtcNow + _parameters.MaximumTimeToWaitForNoOpenFileHandles;

      _openFiles.Add(withTimeout);
      Monitor.PulseAll(_openFilesSync);
    }
  }

  object _openFileHandlePollingSync = new object();

  void PollOpenFilesThread()
  {
    var openFilesCached = new List<SnapshotReferenceWithTimeout>();
    var filesToPromote = new List<SnapshotReferenceWithTimeout>();

    while (true)
    {
      bool haveOpenFiles = false;

      lock (_openFilesSync)
      {
        haveOpenFiles = (_openFiles.Count != 0);

        // If we don't have any open files, wait indefinitely but break as soon as open files become available.
        if (!haveOpenFiles)
        {
          Monitor.Wait(_openFilesSync);

          if (_stopping)
            return;
      
          openFilesCached.Clear();
          openFilesCached.AddRange(_openFiles);
        }
      }

      // As long as we have open files, always wait for the interval between checks.
      if (haveOpenFiles)
      {
        lock (_openFileHandlePollingSync)
          Monitor.Wait(_openFileHandlePollingSync, _parameters.OpenFileHandlePollingInterval);

        if (_stopping)
          return;

        openFilesCached.Clear();

        lock (_openFilesSync)
          openFilesCached.AddRange(_openFiles);
      }

      for (int i = openFilesCached.Count; i >= 0; i--)
      {
        var fileReference = openFilesCached[i];

        if (!_openFileHandles.Enumerate(fileReference.SnapshotReference.Path).Any())
        {
          filesToPromote.Add(fileReference);
          openFilesCached.RemoveAt(i);
        }
      }

      if (filesToPromote.Any())
      {
        lock (_openFilesSync)
          foreach (var reference in filesToPromote)
            _openFiles.Remove(reference);

        lock (_backupQueueSync)
        {
          foreach (var reference in filesToPromote)
            _backupQueue.Add(reference.SnapshotReference);

          Monitor.PulseAll(_backupQueueSync);
        }

        filesToPromote.Clear();
      }

      bool haveTimedOutFiles = false;

      var now = DateTime.UtcNow;

      foreach (var file in openFilesCached)
        if (file.TimeoutUTC < now)
        {
          haveTimedOutFiles = true;
          break;
        }

      if (haveTimedOutFiles)
      {
        lock (_openFilesSync)
        {
          for (int i = _openFiles.Count - 1; i >= 0; i--)
          {
            if (_openFiles[i].TimeoutUTC < now)
            {
              BeginLongPollingStrategy(_openFiles[i].SnapshotReference);
              _openFiles.RemoveAt(i);
            }
          }
        }
      }
    }
  }

  void BeginLongPollingStrategy(SnapshotReference reference)
  {
    // TODO
    /*
     * If the file is still open after 30 seconds, switch to a new polling method:
       1. Create/locate a ZFS snapshot
       2. If the file has no open file handles, then run Submit file procedure and exit.
       3. Wait 30 seconds
       4. Create/locate a ZFS snapshot
       5. Compare the file in the old and new snapshots.
       6. Drop the old snapshot, the new snapshot becomes the new old snapshot.
       7. If the snapshots are identical or the retry limit has been reached, then run Submit file procedure and exit, otherwise go to step 2.
     */
  }

  object _backupQueueSync = new object();
  List<SnapshotReference> _backupQueue = new List<SnapshotReference>();

  void ProcessBackupQueue()
  {
    while (true)
    {
      SnapshotReference reference;

      lock (_backupQueueSync)
      {
        while (_backupQueue.Count == 0)
        {
          Monitor.Wait(_backupQueueSync);

          if (_stopping)
            return;
        }

        reference = _backupQueue[_backupQueue.Count - 1];
        _backupQueue.RemoveAt(_backupQueue.Count - 1);
      }

      FileReference fileReference;

      var stream = File.OpenRead(reference.SnapshottedPath);

      var backedUpFileState = _remoteFileStateCache.GetFileState(reference.Path);
      var currentLocalFileChecksum = FileState.ComputeChecksum(stream);

      if ((backedUpFileState == null) || (currentLocalFileChecksum != backedUpFileState.Checksum))
      {
        if (stream.Length >= _parameters.MaximumFileSizeForStagingCopy)
          fileReference = new FileReference(reference, stream);
        else
        {
          var stagedCopy = _staging.StageFile(reference.SnapshottedPath);

          stream.Dispose();

          fileReference = new FileReference(reference.Path, stagedCopy);
        }

        lock (_uploadQueueSync)
        {
          _uploadQueue.Add(fileReference);
          Monitor.PulseAll(_uploadQueueSync);
        }
      }
      // TODO: check the file's hash
  /*
* Submit file procedure:
  => Given a ZFS snapshot
  => If the file is small (say, <100 MB), copy it to /tmp then delete the snapshot, and upload it from /tmp
  => If the file is large, upload it from the snapshot then delete the snapshot
* Restrict number of concurrent tasks (separately for small vs. large files)

   * */
    }
  }

  class FileReference : IDisposable
  {
    public string Path;
    public SnapshotReference? SnapshotReference;
    public IStagedFile? StagedFile;

    public Stream Stream;

    public FileReference(SnapshotReference snapshotReference, Stream stream)
    {
      this.Path = snapshotReference.Path;
      this.SnapshotReference = snapshotReference;
      this.Stream = stream;
    }

    public FileReference(string path, IStagedFile stagedFile)
    {
      this.Path = path;
      this.StagedFile = stagedFile;
      this.Stream = File.OpenRead(stagedFile.Path);
    }

    public void Dispose()
    {
      SnapshotReference?.Dispose();
      StagedFile?.Dispose();

      Stream.Dispose();
    }
  }
 
  void StartUploadThreads()
  {
    for (int i=0; i < _parameters.UploadThreadCount; i++)
      new Thread(UploadThreadProc).Start();
  }

  object _uploadQueueSync = new object();
  List<FileReference> _uploadQueue = new List<FileReference>();

  void UploadThreadProc()
  {
    while (true)
    {
      FileReference fileToUpload;

      lock (_uploadQueueSync)
      {
        while (_uploadQueue.Count == 0)
        {
          Monitor.Wait(_uploadQueueSync);

          if (_stopping)
            return;
        }

        fileToUpload = _uploadQueue[_uploadQueue.Count - 1];
        _uploadQueue.RemoveAt(_uploadQueue.Count - 1);
      }

      using (fileToUpload) 
      {
        // TODO: write down that we're now uploading this file so that if we get interrupted, we can pick up where we left off
        _storage.UploadFile(Path.Combine("/content", fileToUpload.Path), fileToUpload.Stream);
      }
    }
  }

  public void Start()
  {
    _stopping = false;
    _monitor.Start();
  }

  public void Stop()
  {
    _stopping = true;

    _monitor.Stop();

    // The open file handles polling thread waits on _openFilesSync when its queue is empty, and
    // on _openFileHandlePollingSync when its queue is not empty.
    lock (_openFilesSync)
      Monitor.PulseAll(_openFilesSync);

    lock (_openFileHandlePollingSync)
      Monitor.PulseAll(_openFileHandlePollingSync);

    // The backup queue waits on _backupQueueSync.
    lock (_backupQueueSync)
      Monitor.PulseAll(_backupQueueSync);

    // Upload threads wait on _uploadQueueSync.
    lock (_uploadQueueSync)
      Monitor.PulseAll(_uploadQueueSync);

    // TODO: cancel long operations
    // TODO: wait for short operations to complete
  }
}

