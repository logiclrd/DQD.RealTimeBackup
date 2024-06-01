using System;
using System.IO;
using System.Net;

using DeltaQ.RTB.Bridge;

using DeltaQ.CommandLineParser;

using DeltaQ.RTB.Console.Formatters;

using Konsole = System.Console;
using System.Threading;
using System.Linq;

namespace DeltaQ.RTB.Console
{
	public class Program
	{
		static BridgeClient ConnectToBridge(CommandLineArguments args)
		{
			if (!(args.ConnectTo is string connectTo))
				throw new Exception("ConnectToBridge was called with args.ConnectTo equal to null");

			if (File.Exists(args.ConnectTo!))
				return BridgeClient.ConnectTo(unixEndPoint: connectTo);
			else if (IPEndPoint.TryParse(connectTo, out var ipEndPoint))
				return BridgeClient.ConnectTo(ipEndPoint);
			else
				throw new Exception("Couldn't decode the Connect To address: " + connectTo);
		}

		static int Main()
		{
			var commandLine = new CommandLine();

			var args = commandLine.Parse<CommandLineArguments>();

			if (args.ShowUsage || (args.ConnectTo == null))
			{
				commandLine.ShowUsage<CommandLineArguments>(detailed: args.ShowUsage);

				return 0;
			}

			IOutputFormatter output = (args.XML ? new XMLOutputFormatter() : new TextOutputFormatter());

			BridgeClient connection;

			try
			{
				connection = ConnectToBridge(args);
			}
			catch (Exception e)
			{
				Konsole.Error.WriteLine(e.Message);
				return 1;
			}

			var cancellationTokenSource = new CancellationTokenSource();
			var shutdownEvent = new ManualResetEvent(initialState: false);

			if (args.Persistent)
			{
				Konsole.CancelKeyPress +=
					(sender, e) =>
					{
						cancellationTokenSource.Cancel();
						shutdownEvent.WaitOne();
					};

				AppDomain.CurrentDomain.ProcessExit +=
					(sender, e) =>
					{
						cancellationTokenSource.Cancel();
						shutdownEvent.WaitOne();
					};
			}

			try
			{
				if (args.GetStats)
					Commands.GetStats.Execute(connection, output, args.GetStatsIncludeUploads, args.GetStatsRepeat, cancellationTokenSource.Token);

				if (args.CheckFilePaths.Any())
					foreach (var filePath in args.CheckFilePaths)
						Commands.CheckPath.Execute(connection, output, filePath);

				if (args.PauseMonitor)
					Commands.PauseMonitor.Execute(connection, output);

				if (args.UnpauseMonitor)
					Commands.UnpauseMonitor.Execute(connection, output, args.DiscardBufferedNotifications);
			}
			catch (Exception ex)
			{
				output.EmitError(ex);
			}
			finally
			{
				output.Close();
			}

			shutdownEvent.Set();

			return 0;
		}
	}
}
