using System;
using System.Threading;
using System.Threading.Tasks;
using Cordex.Core;

namespace Cordex;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            System.Windows.MessageBox.Show(
                ex.ExceptionObject?.ToString() ?? "Unknown error",
                "Cordex — Crash",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error
            );
        };

        DispatcherUnhandledException += (_, ex) =>
        {
            System.Windows.MessageBox.Show(
                ex.Exception?.ToString() ?? "Unknown error",
                "Cordex — Dispatcher Crash",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error
            );
            ex.Handled = true;
        };

        _mutex = new Mutex(true, "Cordex_SingleInstance_Mutex", out bool isNew);
        if (!isNew)
        {
            System.Windows.MessageBox.Show(
                "Cordex is already running. Check the system tray.",
                "Cordex",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information
            );
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Check for updates before starting the main window
        CheckVersionAndStart();
    }

    private async void CheckVersionAndStart()
    {
        try
        {
            var versionResult = await VersionManager.CheckVersionAsync();

            // Show update window if app is disabled
            if (versionResult.IsDisabled)
            {
                var updateWindow = new UpdateWindow(versionResult);
                updateWindow.ShowDialog();
                Shutdown();
                return;
            }

            // Show update window if version is not supported
            if (!versionResult.IsSupported)
            {
                var updateWindow = new UpdateWindow(versionResult);
                updateWindow.ShowDialog();
                Shutdown();
                return;
            }

            // Show update window if update is available (optional)
            if (versionResult.UpdateAvailable)
            {
                var updateWindow = new UpdateWindow(versionResult);
                updateWindow.ShowDialog();

                // If user chose to update, the app will shutdown in UpdateWindow
                if (updateWindow.ShouldUpdate)
                {
                    return;
                }
            }

            // Start main window
            StartMainWindow();
        }
        catch (Exception ex)
        {
            // If version check fails, continue with app startup
            System.Diagnostics.Debug.WriteLine($"Version check failed: {ex.Message}");
            StartMainWindow();
        }
    }

    private void StartMainWindow()
    {
        try
        {
            new MainWindow().Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.ToString(),
                "Cordex — Failed to Start",
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
