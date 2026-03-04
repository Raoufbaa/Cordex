using System;
using System.Threading;

namespace Nextcord;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            System.Windows.MessageBox.Show(
                ex.ExceptionObject?.ToString() ?? "Unknown error",
                "Nextcord — Crash",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error
            );
        };

        DispatcherUnhandledException += (_, ex) =>
        {
            System.Windows.MessageBox.Show(
                ex.Exception?.ToString() ?? "Unknown error",
                "Nextcord — Dispatcher Crash",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error
            );
            ex.Handled = true;
        };

        _mutex = new Mutex(true, "Nextcord_SingleInstance_Mutex", out bool isNew);
        if (!isNew)
        {
            System.Windows.MessageBox.Show(
                "Nextcord is already running. Check the system tray.",
                "Nextcord",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information
            );
            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            new MainWindow().Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.ToString(),
                "Nextcord — Failed to Start",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error
            );
            Shutdown();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
