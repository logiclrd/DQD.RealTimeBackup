using System;

using Autofac;

using DeltaQ.CommandLineParser;

namespace DeltaQ.RTB
{
  class Program
  {
    static OperatingParameters BuildOperatingParameters(CommandLineArguments args)
    {
      var parameters = new OperatingParameters();

      // TODO: load from file

      if (args.DisableFAN)
        parameters.EnableFileAccessNotify = false;

      return parameters;
    }

    static IContainer InitializeContainer(OperatingParameters parameters)
    {
      var builder = new ContainerBuilder();

      builder.RegisterInstance(parameters);

      builder.RegisterType<BackupAgent>().AsImplementedInterfaces();
      builder.RegisterType<FileAccessNotify>().AsImplementedInterfaces();
      builder.RegisterType<FileSystemMonitor>().AsImplementedInterfaces();
      builder.RegisterType<MD5Checksum>().AsImplementedInterfaces();
      builder.RegisterType<MountTable>().AsImplementedInterfaces();
      builder.RegisterType<OpenByHandleAt>().AsImplementedInterfaces();
      builder.RegisterType<OpenFileHandles>().AsImplementedInterfaces();
      builder.RegisterType<RemoteFileStateCache>().AsImplementedInterfaces();
      builder.RegisterType<B2RemoteStorage>().AsImplementedInterfaces();
      builder.RegisterType<Staging>().AsImplementedInterfaces();
      builder.RegisterType<Timer>().AsImplementedInterfaces();
      builder.RegisterType<ZFS>().AsImplementedInterfaces();

      return builder.Build();
    }

    static void Main()
    {
      var args = new CommandLine().Parse<CommandLineArguments>();

      var parameters = BuildOperatingParameters(args);

      var container = InitializeContainer(parameters);

      var backupAgent = container.Resolve<IBackupAgent>();

      backupAgent.Start();

      Console.WriteLine("Starting monitor...");
      backupAgent.Start();

      Console.WriteLine("Press enter to stop");
      Console.ReadLine();

      Console.WriteLine("Stopping monitor...");
      backupAgent.Stop();
    }
  }
}

