using System;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using ReactiveUI;

namespace DeltaQ.RTB.UserInterface;

public partial class App : Application
{
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public App()
	{
		ShowWindowCommand = ReactiveCommand.Create(ShowWindow);
		PauseMonitoringCommand = ReactiveCommand.Create(PauseMonitoring);
		ResumeMonitoring_ProcessBufferedEvents_Command = ReactiveCommand.Create(() => ResumeMonitoring(true));
		ResumeMonitoring_DiscardBufferedEvents_Command = ReactiveCommand.Create(() => ResumeMonitoring(false));
		ExitCommand = ReactiveCommand.Create(Exit);

		DataContext = this;
	}

	public ICommand ShowWindowCommand { get; }
	public ICommand PauseMonitoringCommand { get; }
	public ICommand ResumeMonitoring_ProcessBufferedEvents_Command { get; }
	public ICommand ResumeMonitoring_DiscardBufferedEvents_Command { get; }
	public ICommand ExitCommand { get; }

	public MainWindow? MainWindow { get; set; }

	void ShowWindow()
	{
		MainWindow ??= new MainWindow();
		MainWindow.Show();
	}

	void PauseMonitoring()
	{
	}

	void ResumeMonitoring(bool processBufferedEvents)
	{
	}

	void Exit()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			desktop.Shutdown();
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

		base.OnFrameworkInitializationCompleted();

		ShowWindow();
	}
}
