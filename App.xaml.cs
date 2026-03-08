using System;
using System.Threading;
using System.Threading.Tasks;
using Cordex.Core;

namespace Cordex;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;
    private static System.Threading.Timer? _performanceMonitorTimer;
    private static int _performanceMonitorRunning;

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
                
                // User clicked "Later" - continue to main window
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
            // Apply performance settings before starting main window
            try
            {
                PerformanceManager.ApplyPerformanceSettings();
                RefreshPerformanceMonitoring();
            }
            catch (Exception perfEx)
            {
                System.Diagnostics.Debug.WriteLine($"Performance settings failed: {perfEx.Message}");
                // Continue anyway - don't let performance settings stop the app
            }
            
            var mainWindow = new MainWindow();
            
            // Check if should start minimized
            if (SettingsManager.Current.StartMinimized)
            {
                mainWindow.Show();
                mainWindow.Hide(); // Start hidden in tray
            }
            else
            {
                mainWindow.Show();
            }
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

    internal void RefreshPerformanceMonitoring()
    {
        _performanceMonitorTimer?.Dispose();
        _performanceMonitorTimer = null;

        if (!PerformanceManager.RequiresMonitoring())
            return;

        var interval = PerformanceManager.GetMonitoringInterval();
        _performanceMonitorTimer = new System.Threading.Timer(
            _ =>
            {
                if (Interlocked.Exchange(ref _performanceMonitorRunning, 1) == 1)
                    return;

                try
                {
                    PerformanceManager.RunMonitoringCycle();
                }
                finally
                {
                    Volatile.Write(ref _performanceMonitorRunning, 0);
                }
            },
            null,
            interval,
            interval
        );
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _performanceMonitorTimer?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
