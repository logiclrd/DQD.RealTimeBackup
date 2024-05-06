//using System;
//using System.Diagnostics;
//using System.Runtime.InteropServices;
//using System.Text;
//using System.Threading;

//using Microsoft.Win32.SafeHandles;

class Program
{
  static void Main()
  {
    var monitor = new FileSystemMonitor();

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
