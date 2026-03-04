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

    private static readonly WpfBrush _blue   = new(WpfColor.FromRgb(0x00, 0xB0, 0xF4));
    private static readonly WpfBrush _orange = new(WpfColor.FromRgb(0xFF, 0xA5, 0x00));
    private static readonly WpfBrush _activeNav = new(WpfColor.FromRgb(0x40, 0x42, 0x49));

    public SettingsWindow()
    {
        InitializeComponent();

        var s = SettingsManager.Current;
        
        // Load keybinds
        _tempMute        = Clone(s.Mute);
        _tempDeafen      = Clone(s.Deafen);
        _tempFocus       = Clone(s.Focus);
        _tempPushToTalk  = Clone(s.PushToTalk);
        _tempPushToMute  = Clone(s.PushToMute);

        TbMute.Text        = _tempMute.Display;
        TbDeafen.Text      = _tempDeafen.Display;
        TbFocus.Text       = _tempFocus.Display;
        TbPushToTalk.Text  = _tempPushToTalk.Display;
        TbPushToMute.Text  = _tempPushToMute.Display;

        // Load general settings
        ToggleHardwareAccel.IsChecked     = s.HardwareAcceleration;
        ToggleReducedMotion.IsChecked     = s.ReducedMotion;
        ToggleStartWithWindows.IsChecked  = s.StartWithWindows;

        // Load window behavior
        ToggleCloseToTray.IsChecked       = s.CloseToTray;
        ToggleMinimizeToTray.IsChecked    = s.MinimizeToTray;
        ToggleStartMinimized.IsChecked    = s.StartMinimized;

        // Load voice & audio
        ToggleShowVoiceActivity.IsChecked = s.ShowVoiceActivity;
        ToggleAutoMute.IsChecked          = s.AutomaticallyMute;
        SliderVoiceThreshold.Value        = s.VoiceActivityThreshold;
        TxtVoiceThreshold.Text            = $"{s.VoiceActivityThreshold}%";

        // Load notifications
        ToggleShowNotifications.IsChecked = s.ShowNotifications;
        ToggleNotificationSounds.IsChecked = s.NotificationSounds;

        // Set version text
        VersionTextBlock.Text = $"Version {VersionManager.GetCurrentVersion()}";

        // Wire up slider event
        SliderVoiceThreshold.ValueChanged += (s, e) => 
            TxtVoiceThreshold.Text = $"{(int)e.NewValue}%";
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn || btn.Tag is not string tag) return;

        // Hide all pages
        PageGeneral.Visibility        = Visibility.Collapsed;
        PageWindowBehavior.Visibility = Visibility.Collapsed;
        PageVoiceAudio.Visibility     = Visibility.Collapsed;
        PageNotifications.Visibility  = Visibility.Collapsed;
        PageKeybinds.Visibility       = Visibility.Collapsed;
        PageAbout.Visibility          = Visibility.Collapsed;

        // Reset all nav buttons
        NavGeneral.Background        = WpfBrushes.Transparent;
        NavWindowBehavior.Background = WpfBrushes.Transparent;
        NavVoiceAudio.Background     = WpfBrushes.Transparent;
        NavNotifications.Background  = WpfBrushes.Transparent;
        NavKeybinds.Background       = WpfBrushes.Transparent;
        NavAbout.Background          = WpfBrushes.Transparent;

        NavGeneral.Foreground        = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));
        NavWindowBehavior.Foreground = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));
        NavVoiceAudio.Foreground     = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));
        NavNotifications.Foreground  = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));
        NavKeybinds.Foreground       = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));
        NavAbout.Foreground          = new WpfBrush(WpfColor.FromRgb(0x96, 0x98, 0x9D));

        // Show selected page and highlight nav
        switch (tag)
        {
            case "General":
                PageGeneral.Visibility = Visibility.Visible;
                NavGeneral.Background = _activeNav;
                NavGeneral.Foreground = WpfBrushes.White;
                break;
            case "WindowBehavior":
                PageWindowBehavior.Visibility = Visibility.Visible;
                NavWindowBehavior.Background = _activeNav;
                NavWindowBehavior.Foreground = WpfBrushes.White;
                break;
            case "VoiceAudio":
                PageVoiceAudio.Visibility = Visibility.Visible;
                NavVoiceAudio.Background = _activeNav;
                NavVoiceAudio.Foreground = WpfBrushes.White;
                break;
            case "Notifications":
                PageNotifications.Visibility = Visibility.Visible;
                NavNotifications.Background = _activeNav;
                NavNotifications.Foreground = WpfBrushes.White;
                break;
            case "Keybinds":
                PageKeybinds.Visibility = Visibility.Visible;
                NavKeybinds.Background = _activeNav;
                NavKeybinds.Foreground = WpfBrushes.White;
                break;
            case "About":
                PageAbout.Visibility = Visibility.Visible;
                NavAbout.Background = _activeNav;
                NavAbout.Foreground = WpfBrushes.White;
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
        s.HardwareAcceleration = ToggleHardwareAccel.IsChecked == true;
        s.ReducedMotion        = ToggleReducedMotion.IsChecked == true;
        s.StartWithWindows     = ToggleStartWithWindows.IsChecked == true;

        // Save window behavior
        s.CloseToTray       = ToggleCloseToTray.IsChecked == true;
        s.MinimizeToTray    = ToggleMinimizeToTray.IsChecked == true;
        s.StartMinimized    = ToggleStartMinimized.IsChecked == true;

        // Save voice & audio
        s.ShowVoiceActivity      = ToggleShowVoiceActivity.IsChecked == true;
        s.AutomaticallyMute      = ToggleAutoMute.IsChecked == true;
        s.VoiceActivityThreshold = (int)SliderVoiceThreshold.Value;

        // Save notifications
        s.ShowNotifications    = ToggleShowNotifications.IsChecked == true;
        s.NotificationSounds   = ToggleNotificationSounds.IsChecked == true;

        // Handle Start with Windows registry
        SetStartWithWindows(s.StartWithWindows);

        SettingsManager.Save();
        KeybindsSaved?.Invoke();
        SettingsChanged?.Invoke();

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
        CheckUpdateButton.Content = "Checking...";

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
            CheckUpdateButton.Content = "Check for Updates";
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
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
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
}
