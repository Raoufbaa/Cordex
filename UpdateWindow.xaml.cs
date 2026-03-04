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
        ProgressLabelPanel.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        ProgressBar.IsIndeterminate = false;
        ProgressLabel.Text = "Downloading update  ";
        ProgressText.Text = "0%";

        await Task.Delay(100);

        try
        {
            var success = await VersionManager.DownloadAndInstallUpdateAsync(
                _versionResult.DownloadUrl,
                progress =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (progress < 0)
                        {
                            ProgressBar.IsIndeterminate = true;
                            ProgressLabel.Text = "Please wait  ";
                            ProgressText.Text = "...";
                        }
                        else
                        {
                            ProgressBar.IsIndeterminate = false;
                            ProgressBar.Value = progress;
                            ProgressLabel.Text = "Downloading update  ";
                            ProgressText.Text = $"{progress}%";
                        }
                    });
                });

            if (success)
            {
                Dispatcher.Invoke(() =>
                {
                    ProgressBar.IsIndeterminate = false;
                    ProgressBar.Value = 100;
                    ProgressLabel.Text = "Installing update  ";
                    ProgressText.Text = "100%";
                });

                await Task.Delay(1000);
                ShouldUpdate = true;
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                OfferBrowserFallback();
                ResetProgressUI();
            }
        }
        catch (Exception ex)
        {
            OfferBrowserFallback(ex.Message);
            ResetProgressUI();
        }
    }

    private void ResetProgressUI()
    {
        UpdateButton.IsEnabled = true;
        LaterButton.IsEnabled = true;
        ProgressPanel.Visibility = Visibility.Collapsed;
        ProgressLabelPanel.Visibility = Visibility.Collapsed;
        ProgressBar.Value = 0;
        ProgressBar.IsIndeterminate = false;
        _isDownloading = false;
    }

    private void OfferBrowserFallback(string? errorMessage = null)
    {
        var msg = errorMessage != null
            ? $"An error occurred: {errorMessage}\n\nWould you like to open the download page in your browser?"
            : "Failed to download the update automatically.\n\nWould you like to open the download page in your browser?";

        var result = System.Windows.MessageBox.Show(msg,
            errorMessage != null ? "Update Error" : "Update Failed",
            System.Windows.MessageBoxButton.YesNo,
            errorMessage != null ? System.Windows.MessageBoxImage.Error
                                 : System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _versionResult.DownloadUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                System.Windows.MessageBox.Show(
                    $"Please visit:\n{_versionResult.DownloadUrl}",
                    "Download URL",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_versionResult.IsDisabled || !_versionResult.IsSupported)
            System.Windows.Application.Current.Shutdown();
        else
            Close();
    }
}