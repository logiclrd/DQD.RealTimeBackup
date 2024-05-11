using System;

namespace DeltaQ.RTB
{
  class Program
  {
    static void Main()
    {
      var monitor = new FileSystemMonitor(new MountTable());

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

