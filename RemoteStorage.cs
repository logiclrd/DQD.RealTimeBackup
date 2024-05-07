public abstract class RemoteStorage
{
  public abstract void UploadFile(string serverPath, Stream content);
  public abstract void DeleteFile(string serverPath);
}

