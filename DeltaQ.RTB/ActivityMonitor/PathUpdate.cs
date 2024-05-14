namespace DeltaQ.RTB.ActivityMonitor
{
  public class PathUpdate
  {
    public string Path;
    public UpdateType UpdateType;

    public PathUpdate() { Path = ""; }

    public PathUpdate(string path, UpdateType updateType)
    {
      Path = path;
      UpdateType = updateType;
    }
  }
}

