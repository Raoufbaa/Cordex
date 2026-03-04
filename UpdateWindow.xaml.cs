using System;
using System.Windows;
using Cordex.Core;
using Wpf.Ui.Controls;

namespace Cordex;

public partial class UpdateWindow : FluentWindow
{
    private readonly VersionCheckResult _versionResult;
    private bool _isDownloading = false;

    public bool ShouldUpdate { get; private set; } = false;

    public UpdateWindow(VersionCheckResult versionResult)
    {
        InitializeComponent();
        _versionResult = versionResult;
        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        if (_versionResult.IsDisabled)
        {
            // App is disabled
            IconSymbol.Symbol = SymbolRegular.ErrorCircle24;
            IconSymbol.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF2, 0x3F, 0x42));
            TitleText.Text = "App Disabled";
            VersionText.Text = _versionResult.Message;
            ReleaseNotesText.Text = "Please check back later.";
            UpdateButton.Visibility = Visibility.Collapsed;
            LaterButton.Content = "Exit";
        }
        else if (!_versionResult.IsSupported)
        {
            // Version not supported
            IconSymbol.Symbol = SymbolRegular.Warning24;
            IconSymbol.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00));
            TitleText.Text = "Update Required";
            VersionText.Text = _versionResult.Message;
            ReleaseNotesText.Text = _versionResult.ReleaseNotes;
            LaterButton.Content = "Exit";
        }
        else if (_versionResult.UpdateAvailable)
        {
            // Update available
            IconSymbol.Symbol = SymbolRegular.ArrowDownload24;
            TitleText.Text = "Update Available";
            VersionText.Text = $"Version {_versionResult.LatestVersion} is now available (Current: {_versionResult.CurrentVersion})";
            ReleaseNotesText.Text = _versionResult.ReleaseNotes;
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading) return;

        _isDownloading = true;
        UpdateButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        try
        {
            var success = await VersionManager.DownloadAndInstallUpdateAsync(
                _versionResult.DownloadUrl,
                progress =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Value = progress;
                        ProgressText.Text = $"Downloading update... {progress}%";
                    });
                });

            if (success)
            {
                ShouldUpdate = true;
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "Failed to download the update. Please download it manually from the website.",
                    "Update Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
                _isDownloading = false;
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"An error occurred while updating: {ex.Message}",
                "Update Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            UpdateButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            ProgressPanel.Visibility = Visibility.Collapsed;
            _isDownloading = false;
        }
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_versionResult.IsDisabled || !_versionResult.IsSupported)
        {
            // Force exit if app is disabled or version not supported
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            // Just close the dialog
            Close();
        }
    }
}
