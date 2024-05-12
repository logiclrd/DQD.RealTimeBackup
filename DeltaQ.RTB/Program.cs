using System;

using Autofac;

namespace DeltaQ.RTB
{
  class Program
  {
    static IContainer InitializeContainer()
    {
      var builder = new ContainerBuilder();

      builder.RegisterType<FileAccessNotify>().AsImplementedInterfaces();
      builder.RegisterType<FileSystemMonitor>().AsImplementedInterfaces();
      builder.RegisterType<MountTable>().AsImplementedInterfaces();
      builder.RegisterType<OpenByHandleAt>().AsImplementedInterfaces();

      return builder.Build();
    }

    static void Main()
    {
      var container = InitializeContainer();

      var monitor = container.Resolve<IFileSystemMonitor>();

      monitor.PathUpdate +=
        (sender, e) =>
        {
          Console.WriteLine("{0}: {1}", e.UpdateType, e.Path);
        };

      monitor.PathMove +=
        (sender, e) =>
        {
          Console.WriteLine("{0}: {1}", e.MoveType, e.ContainerPath);
        };

      Console.WriteLine("Starting monitor...");
      monitor.Start();

      Console.WriteLine("Press enter to stop");
      Console.ReadLine();

      Console.WriteLine("Stopping monitor...");
      monitor.Stop();
    }
  }
}

