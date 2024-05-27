using System;
using System.Threading;

using DeltaQ.RTB.ActivityMonitor;
using DeltaQ.RTB.Interop;
using DeltaQ.RTB.SurfaceArea;
using DeltaQ.RTB.Utility;

namespace DeltaQ.RTB.FileActivityTrace
{
	class Program
	{
		static void Main()
		{
			var parameters = new OperatingParameters();

			var stopping = new ManualResetEvent(initialState: false);
			var stopped = new ManualResetEvent(initialState: false);

			var mountTable = new MountTable();

			var surfaceArea = new SurfaceAreaImplementation(parameters, mountTable);
			var openByHandleAt = new OpenByHandleAt();

			var fsm = new FileSystemMonitor(
				parameters,
				surfaceArea,
				mountTable,
				() => new FileAccessNotify(parameters),
				openByHandleAt);

			Console.CancelKeyPress +=
				(sender, e) =>
				{
					Console.WriteLine("Stopping");
					stopping.Set();
					fsm.Stop();
					stopped.WaitOne();
					Console.WriteLine("Releasing");
				};

			fsm.PathUpdate +=
				(sender, e) =>
				{
					Console.WriteLine("Path updated: {0}", e.Path);
				};
			
			fsm.PathMove +=
				(sender, e) =>
				{
					Console.WriteLine("Path moved:");
					Console.WriteLine("  From: {0}", e.PathFrom);
					Console.WriteLine("  To: {0}", e.PathTo);
				};

			fsm.PathDelete +=
				(sender, e) =>
				{
					Console.WriteLine("Path deleted: {0}", e.Path);
				};

			Console.WriteLine("Building surface area");

			using (new DiagnosticOutputHook(surfaceArea, Console.WriteLine))
				surfaceArea.BuildDefault();

			Console.WriteLine("Monitoring");

			using (new DiagnosticOutputHook(fsm, Console.WriteLine))
				fsm.Start();

			stopping.WaitOne();

			Console.WriteLine("Stopped");

			stopped.Set();
		}
	}
}
