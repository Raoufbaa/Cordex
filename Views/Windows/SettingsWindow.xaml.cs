using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Cordex.Core;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using WpfColor   = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfBrush   = System.Windows.Media.SolidColorBrush;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfButton  = System.Windows.Controls.Button;


namespace Cordex;


public partial class SettingsWindow : FluentWindow
{
    public event Action? KeybindsSaved;
    public event Action? SettingsChanged;


    private HotkeyConfig _tempMute;
    private HotkeyConfig _tempDeafen;
    private HotkeyConfig _tempFocus;
    private HotkeyConfig _tempPushToTalk;
    private HotkeyConfig _tempPushToMute;


    private WpfTextBox? _activeBox;


    private static readonly WpfBrush _blue     = new(WpfColor.FromRgb(0x00, 0xB0, 0xF4));
    private static readonly WpfBrush _orange   = new(WpfColor.FromRgb(0xFF, 0xA5, 0x00));
    private static readonly WpfBrush _activeNav = new(WpfColor.FromRgb(0x40, 0x42, 0x49));


    public SettingsWindow()
    {
        InitializeComponent();

        var s = SettingsManager.Current;

        // Load keybinds
        _tempMute       = Clone(s.Mute);
        _tempDeafen     = Clone(s.Deafen);
        _tempFocus      = Clone(s.Focus);
        _tempPushToTalk = Clone(s.PushToTalk);
        _tempPushToMute = Clone(s.PushToMute);

        TbMute.Text       = _tempMute.Display;
        TbDeafen.Text     = _tempDeafen.Display;
        TbFocus.Text      = _tempFocus.Display;
        TbPushToTalk.Text = _tempPushToTalk.Display;
        TbPushToMute.Text = _tempPushToMute.Display;

        // Load general settings
        ToggleStartWithWindows.IsChecked = s.StartWithWindows;
        ToggleConfirmExit.IsChecked      = s.ConfirmOnExit;

        // Load window behavior (merged into General)
        ToggleCloseToTray.IsChecked    = s.CloseToTray;
        ToggleMinimizeToTray.IsChecked = s.MinimizeToTray;
        ToggleStartMinimized.IsChecked = s.StartMinimized;

        // Load voice & audio
        ToggleShowVoiceActivity.IsChecked = s.ShowVoiceActivity;
        ToggleAutoMute.IsChecked          = s.AutomaticallyMute;
        SliderVoiceThreshold.Value        = s.VoiceActivityThreshold;
        TxtVoiceThreshold.Text            = $"{s.VoiceActivityThreshold}%";

        // Load notifications
        ToggleShowNotifications.IsChecked  = s.ShowNotifications;
        ToggleNotificationSounds.IsChecked = s.NotificationSounds;

        // Load performance settings
        ToggleHardwareAccel.IsChecked     = s.HardwareAcceleration;
        ToggleReducedMotion.IsChecked     = s.ReducedMotion;
        TogglePerformanceLimits.IsChecked = s.EnablePerformanceLimits;
        SliderCpuCores.Maximum            = Environment.ProcessorCount;
        SliderCpuCores.Value              = Math.Min(s.MaxCpuCores, Environment.ProcessorCount);
        TxtCpuCores.Text                  = $"{(int)SliderCpuCores.Value} cores";
        SliderRamLimit.Value              = s.MaxRamMB;
        TxtRamLimit.Text                  = $"{s.MaxRamMB} MB";
        ToggleLimitGpu.IsChecked          = s.LimitGpuUsage;
        ToggleReduceBackground.IsChecked  = s.ReduceBackgroundActivity;

        UpdatePerformanceLimitsUI(s.EnablePerformanceLimits);
        InitializeCpuAffinityCheckboxes();

        // Load API blocking
        ToggleBlockFingerprinting.IsChecked  = s.BlockFingerprinting;
        ToggleBlockTelemetry.IsChecked       = s.BlockTelemetry;
        ToggleBlockSentry.IsChecked          = s.BlockSentry;
        ToggleBlockTypingIndicator.IsChecked = s.BlockTypingIndicator;
        ToggleBlockAnimatedAssets.IsChecked  = s.BlockAnimatedAssets;
        ToggleBlockCrashReports.IsChecked    = s.BlockCrashReports;

        // Load performance blocking
        ToggleBlockExperiments.IsChecked      = s.BlockExperiments;
        ToggleBlockMarketing.IsChecked        = s.BlockMarketing;
        ToggleBlockDetectableGames.IsChecked  = s.BlockDetectableGames;
        ToggleBlockExternalImages.IsChecked   = s.BlockExternalImages;
        ToggleBlockStatusPolling.IsChecked    = s.BlockStatusPolling;
        ToggleBlockContentInventory.IsChecked = s.BlockContentInventory;
        ToggleBlockVendorChunks.IsChecked     = s.BlockVendorChunks;
        ToggleBlockDiscordStore.IsChecked     = s.BlockDiscordStore;
        ToggleBlockUserSurveys.IsChecked      = s.BlockUserSurveys;
        ToggleBlockStickerPacks.IsChecked     = s.BlockStickerPacks;

        // Set version text
        VersionTextBlock.Text = $"Version {VersionManager.GetCurrentVersion()}";

        // Wire up slider events
        SliderVoiceThreshold.ValueChanged += (_, e) =>
            TxtVoiceThreshold.Text = $"{(int)e.NewValue}%";
    }


    // ── Navigation ────────────────────────────────────────────────────────────


    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn || btn.Tag is not string tag) return;

        // Hide all pages
        PageGeneral.Visibility       = Visibility.Collapsed;
        PageVoiceAudio.Visibility    = Visibility.Collapsed;
        PageNotifications.Visibility = Visibility.Collapsed;
        PagePerformance.Visibility   = Visibility.Collapsed;
        PageKeybinds.Visibility      = Visibility.Collapsed;
        PageAbout.Visibility         = Visibility.Collapsed;

        // Reset all nav buttons
        NavGeneral.Background       = WpfBrushes.Transparent;
        NavVoiceAudio.Background    = WpfBrushes.Transparent;
        NavNotifications.Background = WpfBrushes.Transparent;
        NavPerformance.Background   = WpfBrushes.Transparent;
        NavKeybinds.Background      = WpfBrushes.Transparent;
        NavAbout.Background         = WpfBrushes.Transparent;

        NavGeneral.Foreground       = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));
        NavVoiceAudio.Foreground    = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));
        NavNotifications.Foreground = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));
        NavPerformance.Foreground   = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));
        NavKeybinds.Foreground      = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));
        NavAbout.Foreground         = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));

        // Show selected page and highlight nav
        switch (tag)
        {
            case "General":
                PageGeneral.Visibility = Visibility.Visible;
                NavGeneral.Background  = _activeNav;
                NavGeneral.Foreground  = WpfBrushes.White;
                break;
            case "VoiceAudio":
                PageVoiceAudio.Visibility = Visibility.Visible;
                NavVoiceAudio.Background  = _activeNav;
                NavVoiceAudio.Foreground  = WpfBrushes.White;
                break;
            case "Notifications":
                PageNotifications.Visibility = Visibility.Visible;
                NavNotifications.Background  = _activeNav;
                NavNotifications.Foreground  = WpfBrushes.White;
                break;
            case "Performance":
                PagePerformance.Visibility = Visibility.Visible;
                NavPerformance.Background  = _activeNav;
                NavPerformance.Foreground  = WpfBrushes.White;
                break;
            case "Keybinds":
                PageKeybinds.Visibility = Visibility.Visible;
                NavKeybinds.Background  = _activeNav;
                NavKeybinds.Foreground  = WpfBrushes.White;
                break;
            case "About":
                PageAbout.Visibility = Visibility.Visible;
                NavAbout.Background  = _activeNav;
                NavAbout.Foreground  = WpfBrushes.White;
                break;
        }
    }


    // ── Keybind Capture ───────────────────────────────────────────────────────


    private void KeybindBox_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not WpfTextBox tb) return;
        _activeBox    = tb;
        tb.Text       = "Press keys...";
        tb.Foreground = _orange;
        tb.Focus();
    }


    private void KeybindBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not WpfTextBox tb || _activeBox != tb) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl  or Key.RightCtrl  or
                   Key.LeftShift or Key.RightShift  or
                   Key.LeftAlt   or Key.RightAlt    or
                   Key.LWin      or Key.RWin)
            return;

        var mods    = Keyboard.Modifiers;
        uint winMod = 0;
        if (mods.HasFlag(ModifierKeys.Control)) winMod |= 0x0002;
        if (mods.HasFlag(ModifierKeys.Shift))   winMod |= 0x0004;
        if (mods.HasFlag(ModifierKeys.Alt))     winMod |= 0x0001;

        var vk      = (uint)KeyInterop.VirtualKeyFromKey(key);
        var display = BuildDisplay(mods, key);
        var config  = new HotkeyConfig { Modifiers = winMod, VirtualKey = vk, Display = display };

        switch (tb.Tag?.ToString())
        {
            case "Mute":       _tempMute       = config; break;
            case "Deafen":     _tempDeafen     = config; break;
            case "Focus":      _tempFocus      = config; break;
            case "PushToTalk": _tempPushToTalk = config; break;
            case "PushToMute": _tempPushToMute = config; break;
        }

        tb.Text       = display;
        tb.Foreground = _blue;
        _activeBox    = null;
        Keyboard.ClearFocus();
    }


    private void KeybindBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfTextBox tb || _activeBox != tb) return;
        tb.Text = tb.Tag?.ToString() switch
        {
            "Mute"       => _tempMute.Display,
            "Deafen"     => _tempDeafen.Display,
            "Focus"      => _tempFocus.Display,
            "PushToTalk" => _tempPushToTalk.Display,
            "PushToMute" => _tempPushToMute.Display,
            _            => tb.Text
        };
        tb.Foreground = _blue;
        _activeBox    = null;
    }


    // ── Reset Keybinds ────────────────────────────────────────────────────────


    private void ResetMute_Click(object sender, RoutedEventArgs e)
    {
        _tempMute   = new HotkeyConfig { Modifiers = 0x0006, VirtualKey = 0x4D, Display = "Ctrl+Shift+M" };
        TbMute.Text = _tempMute.Display;
    }


    private void ResetDeafen_Click(object sender, RoutedEventArgs e)
    {
        _tempDeafen   = new HotkeyConfig { Modifiers = 0x0006, VirtualKey = 0x44, Display = "Ctrl+Shift+D" };
        TbDeafen.Text = _tempDeafen.Display;
    }


    private void ResetFocus_Click(object sender, RoutedEventArgs e)
    {
        _tempFocus   = new HotkeyConfig { Modifiers = 0x0006, VirtualKey = 0x4E, Display = "Ctrl+Shift+N" };
        TbFocus.Text = _tempFocus.Display;
    }


    private void ResetPushToTalk_Click(object sender, RoutedEventArgs e)
    {
        _tempPushToTalk   = new HotkeyConfig { Modifiers = 0x0000, VirtualKey = 0xC0, Display = "`" };
        TbPushToTalk.Text = _tempPushToTalk.Display;
    }


    private void ResetPushToMute_Click(object sender, RoutedEventArgs e)
    {
        _tempPushToMute   = new HotkeyConfig { Modifiers = 0x0000, VirtualKey = 0x00, Display = "Not Set" };
        TbPushToMute.Text = _tempPushToMute.Display;
    }


    // ── Save / Cancel ─────────────────────────────────────────────────────────


    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsManager.Current;

        // Check if hardware acceleration changed
        bool hwAccelChanged = s.HardwareAcceleration != (ToggleHardwareAccel.IsChecked == true);

        // Save keybinds
        s.Mute       = _tempMute;
        s.Deafen     = _tempDeafen;
        s.Focus      = _tempFocus;
        s.PushToTalk = _tempPushToTalk;
        s.PushToMute = _tempPushToMute;

        // Save general settings
        s.StartWithWindows = ToggleStartWithWindows.IsChecked == true;
        s.ConfirmOnExit    = ToggleConfirmExit.IsChecked == true;

        // Save window behavior (merged into General)
        s.CloseToTray    = ToggleCloseToTray.IsChecked == true;
        s.MinimizeToTray = ToggleMinimizeToTray.IsChecked == true;
        s.StartMinimized = ToggleStartMinimized.IsChecked == true;

        // Save voice & audio
        s.ShowVoiceActivity      = ToggleShowVoiceActivity.IsChecked == true;
        s.AutomaticallyMute      = ToggleAutoMute.IsChecked == true;
        s.VoiceActivityThreshold = (int)SliderVoiceThreshold.Value;

        // Save notifications
        s.ShowNotifications  = ToggleShowNotifications.IsChecked == true;
        s.NotificationSounds = ToggleNotificationSounds.IsChecked == true;

        // Save performance settings
        s.HardwareAcceleration     = ToggleHardwareAccel.IsChecked == true;
        s.ReducedMotion            = ToggleReducedMotion.IsChecked == true;
        s.EnablePerformanceLimits  = TogglePerformanceLimits.IsChecked == true;
        s.MaxCpuCores              = (int)SliderCpuCores.Value;
        s.MaxCpuPercent            = 100;
        s.MaxRamMB                 = (int)SliderRamLimit.Value;
        s.LimitGpuUsage            = ToggleLimitGpu.IsChecked == true;
        s.ReduceBackgroundActivity = ToggleReduceBackground.IsChecked == true;

        // Save CPU affinity from checkboxes
        SaveCpuAffinityFromCheckboxes();

        // Save API blocking
        s.BlockFingerprinting   = ToggleBlockFingerprinting.IsChecked == true;
        s.BlockTelemetry        = ToggleBlockTelemetry.IsChecked == true;
        s.BlockSentry           = ToggleBlockSentry.IsChecked == true;
        s.BlockTypingIndicator  = ToggleBlockTypingIndicator.IsChecked == true;
        s.BlockAnimatedAssets   = ToggleBlockAnimatedAssets.IsChecked == true;
        s.BlockCrashReports     = ToggleBlockCrashReports.IsChecked == true;

        // Save performance blocking
        s.BlockExperiments      = ToggleBlockExperiments.IsChecked == true;
        s.BlockMarketing        = ToggleBlockMarketing.IsChecked == true;
        s.BlockDetectableGames  = ToggleBlockDetectableGames.IsChecked == true;
        s.BlockExternalImages   = ToggleBlockExternalImages.IsChecked == true;
        s.BlockStatusPolling    = ToggleBlockStatusPolling.IsChecked == true;
        s.BlockContentInventory = ToggleBlockContentInventory.IsChecked == true;
        s.BlockVendorChunks     = ToggleBlockVendorChunks.IsChecked == true;
        s.BlockDiscordStore     = ToggleBlockDiscordStore.IsChecked == true;
        s.BlockUserSurveys      = ToggleBlockUserSurveys.IsChecked == true;
        s.BlockStickerPacks     = ToggleBlockStickerPacks.IsChecked == true;

        // Handle Start with Windows registry
        SetStartWithWindows(s.StartWithWindows);

        SettingsManager.Save();
        KeybindsSaved?.Invoke();
        SettingsChanged?.Invoke();

        // Apply performance settings immediately
        if (s.EnablePerformanceLimits)
            PerformanceManager.ApplyPerformanceSettings();

        // Show restart message if hardware acceleration changed
        if (hwAccelChanged)
        {
            System.Windows.MessageBox.Show(
                "Hardware acceleration changes require a restart to take effect.",
                "Restart Required",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        Close();
    }


    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();


    // ── Check for Updates ─────────────────────────────────────────────────────


    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content   = "Checking...";

        try
        {
            var versionResult = await VersionManager.CheckVersionAsync();

            if (versionResult.IsDisabled || !versionResult.IsSupported || versionResult.UpdateAvailable)
            {
                var updateWindow = new UpdateWindow(versionResult);
                updateWindow.ShowDialog();
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "You are running the latest version of Cordex!",
                    "No Updates Available",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to check for updates: {ex.Message}",
                "Update Check Failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButton.Content   = "Check for Updates";
        }
    }


    // ── Helpers ───────────────────────────────────────────────────────────────


    private static string BuildDisplay(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift))   parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt))     parts.Add("Alt");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }


    private static HotkeyConfig Clone(HotkeyConfig src)
        => new() { Modifiers = src.Modifiers, VirtualKey = src.VirtualKey, Display = src.Display };


    private void SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            const string appName = "Cordex";
            if (enable)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(appName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        catch { }
    }


    // ── Performance Settings ──────────────────────────────────────────────────


    private void TogglePerformanceLimits_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = TogglePerformanceLimits.IsChecked == true;
        UpdatePerformanceLimitsUI(enabled);
    }


    private void UpdatePerformanceLimitsUI(bool enabled)
    {
        BorderCpuCores.IsEnabled           = enabled;
        BorderRamLimit.IsEnabled           = enabled;
        BorderCpuAffinity.IsEnabled        = enabled;
        BorderGpuLimit.IsEnabled           = enabled;
        BorderBackgroundActivity.IsEnabled = enabled;
    }


    private void SliderCpuCores_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtCpuCores != null)
            TxtCpuCores.Text = $"{(int)e.NewValue} cores";
    }


    private void SliderRamLimit_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtRamLimit != null)
            TxtRamLimit.Text = $"{(int)e.NewValue} MB";
    }





    private void BtnExpandCpuAffinity_Click(object sender, RoutedEventArgs e)
    {
        if (PanelCpuCores.Visibility == Visibility.Collapsed)
        {
            PanelCpuCores.Visibility     = Visibility.Visible;
            BtnExpandCpuAffinity.Content = "▲";
        }
        else
        {
            PanelCpuCores.Visibility     = Visibility.Collapsed;
            BtnExpandCpuAffinity.Content = "▼";
        }
    }


    private void InitializeCpuAffinityCheckboxes()
    {
        WrapPanelCpuCores.Children.Clear();
        int  coreCount   = Environment.ProcessorCount;
        long currentMask = SettingsManager.Current.CpuAffinityMask;

        for (int i = 0; i < coreCount; i++)
        {
            var checkBox = new System.Windows.Controls.CheckBox
            {
                Content    = $"Core {i}",
                Foreground = WpfBrushes.White,
                FontSize   = 12,
                Margin     = new Thickness(0, 0, 12, 8),
                IsChecked  = currentMask == 0 || (currentMask & (1L << i)) != 0,
                Tag        = i
            };
            WrapPanelCpuCores.Children.Add(checkBox);
        }
    }


    private void SaveCpuAffinityFromCheckboxes()
    {
        long mask       = 0;
        bool anyChecked = false;

        foreach (var child in WrapPanelCpuCores.Children)
        {
            if (child is System.Windows.Controls.CheckBox cb && cb.IsChecked == true)
            {
                int coreIndex = (int)cb.Tag;
                mask |= (1L << coreIndex);
                anyChecked = true;
            }
        }

        SettingsManager.Current.CpuAffinityMask  = anyChecked ? mask : 0;
        SettingsManager.Current.EnableCpuAffinity = anyChecked;
    }


    private void BtnCpuAffinity_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CpuAffinityDialog(SettingsManager.Current.CpuAffinityMask);
        if (dialog.ShowDialog() == true)
        {
            SettingsManager.Current.CpuAffinityMask  = dialog.AffinityMask;
            SettingsManager.Current.EnableCpuAffinity = dialog.AffinityMask != 0;
        }
    }
}


// ── CPU Affinity Dialog ───────────────────────────────────────────────────────


public class CpuAffinityDialog : FluentWindow
{
    public long AffinityMask { get; private set; }
    private readonly System.Windows.Controls.CheckBox[] _checkBoxes;

    public CpuAffinityDialog(long currentMask)
    {
        Title                 = "CPU Affinity";
        Width                 = 400;
        Height                = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode            = ResizeMode.NoResize;
        Background            = new WpfBrush(WpfColor.FromRgb(0x1E, 0x1F, 0x22));

        AffinityMask = currentMask;
        int coreCount = Environment.ProcessorCount;
        _checkBoxes   = new System.Windows.Controls.CheckBox[coreCount];

        var mainGrid = new System.Windows.Controls.Grid();
        mainGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            { Height = GridLength.Auto });

        var scrollViewer = new System.Windows.Controls.ScrollViewer
        {
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Margin = new Thickness(20)
        };

        var stackPanel = new System.Windows.Controls.StackPanel();

        stackPanel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text       = "Select CPU Cores",
            FontSize   = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = WpfBrushes.White,
            Margin     = new Thickness(0, 0, 0, 10)
        });

        stackPanel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text         = "Choose which CPU cores the application can use:",
            FontSize     = 12,
            Foreground   = new WpfBrush(WpfColor.FromRgb(0x80, 0x84, 0x8E)),
            Margin       = new Thickness(0, 0, 0, 20),
            TextWrapping = TextWrapping.Wrap
        });

        for (int i = 0; i < coreCount; i++)
        {
            var checkBox = new System.Windows.Controls.CheckBox
            {
                Content    = $"Core {i}",
                Foreground = WpfBrushes.White,
                FontSize   = 13,
                Margin     = new Thickness(0, 0, 0, 8),
                IsChecked  = currentMask == 0 || (currentMask & (1L << i)) != 0
            };
            _checkBoxes[i] = checkBox;
            stackPanel.Children.Add(checkBox);
        }

        scrollViewer.Content = stackPanel;
        System.Windows.Controls.Grid.SetRow(scrollViewer, 0);
        mainGrid.Children.Add(scrollViewer);

        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation         = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin              = new Thickness(20, 10, 20, 20)
        };

        var cancelBtn = new Wpf.Ui.Controls.Button
        {
            Content    = "Cancel",
            Width      = 90,
            Height     = 36,
            Margin     = new Thickness(0, 0, 10, 0),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

        var saveBtn = new Wpf.Ui.Controls.Button
        {
            Content    = "Apply",
            Width      = 90,
            Height     = 36,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary
        };
        saveBtn.Click += (_, _) =>
        {
            long mask = 0;
            for (int i = 0; i < _checkBoxes.Length; i++)
            {
                if (_checkBoxes[i].IsChecked == true)
                    mask |= (1L << i);
            }
            AffinityMask = mask;
            DialogResult = true;
            Close();
        };

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(saveBtn);

        System.Windows.Controls.Grid.SetRow(buttonPanel, 1);
        mainGrid.Children.Add(buttonPanel);

        Content = mainGrid;
    }
}
