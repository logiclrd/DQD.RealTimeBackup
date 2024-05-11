namespace DeltaQ.RTB
{
  public class PathMove
  {
    public string ContainerPath;
    public MoveType MoveType;

    public PathMove() { ContainerPath = ""; }

    public PathMove(string path, MoveType moveType)
    {
      ContainerPath = path;
      MoveType = moveType;
    }
  }
}

