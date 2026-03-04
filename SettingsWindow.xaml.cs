using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Cordex.Core;
using Wpf.Ui.Controls;
using WpfColor   = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfBrush   = System.Windows.Media.SolidColorBrush;

namespace Cordex;

public partial class SettingsWindow : FluentWindow
{
    public event Action? KeybindsSaved;

    private HotkeyConfig _tempMute;
    private HotkeyConfig _tempDeafen;
    private HotkeyConfig _tempFocus;

    private System.Windows.Controls.TextBox? _activeBox;

    private static readonly WpfBrush _blue   = new(WpfColor.FromRgb(0x00, 0xB0, 0xF4));
    private static readonly WpfBrush _orange = new(WpfColor.FromRgb(0xFF, 0xA5, 0x00));

    public SettingsWindow()
    {
        InitializeComponent();

        var s       = SettingsManager.Current;
        _tempMute   = Clone(s.Mute);
        _tempDeafen = Clone(s.Deafen);
        _tempFocus  = Clone(s.Focus);

        TbMute.Text   = _tempMute.Display;
        TbDeafen.Text = _tempDeafen.Display;
        TbFocus.Text  = _tempFocus.Display;

        // Set version text
        VersionTextBlock.Text = $"Version {VersionManager.GetCurrentVersion()}";
    }

    // ── Nav ───────────────────────────────────────────────────────────────────

    private void NavKeybinds_Click(object sender, RoutedEventArgs e)
    {
        PageKeybinds.Visibility = Visibility.Visible;
        PageAbout.Visibility    = Visibility.Collapsed;
        NavKeybinds.Background  = new WpfBrush(WpfColor.FromRgb(0x40, 0x42, 0x49));
        NavAbout.Background     = WpfBrushes.Transparent;
    }

    private void NavAbout_Click(object sender, RoutedEventArgs e)
    {
        PageKeybinds.Visibility = Visibility.Collapsed;
        PageAbout.Visibility    = Visibility.Visible;
        NavAbout.Background     = new WpfBrush(WpfColor.FromRgb(0x40, 0x42, 0x49));
        NavKeybinds.Background  = WpfBrushes.Transparent;
    }

    // ── Keybind Capture ───────────────────────────────────────────────────────

    private void KeybindBox_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb) return;
        _activeBox    = tb;
        tb.Text       = "Press keys...";
        tb.Foreground = _orange;
        tb.Focus();
    }

    private void KeybindBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb || _activeBox != tb) return;
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
            case "Mute":   _tempMute   = config; break;
            case "Deafen": _tempDeafen = config; break;
            case "Focus":  _tempFocus  = config; break;
        }

        tb.Text       = display;
        tb.Foreground = _blue;
        _activeBox    = null;
        Keyboard.ClearFocus();
    }

    private void KeybindBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb || _activeBox != tb) return;
        tb.Text = tb.Tag?.ToString() switch
        {
            "Mute"   => _tempMute.Display,
            "Deafen" => _tempDeafen.Display,
            "Focus"  => _tempFocus.Display,
            _        => tb.Text
        };
        tb.Foreground = _blue;
        _activeBox    = null;
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

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

    // ── Save / Cancel ─────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.Mute   = _tempMute;
        SettingsManager.Current.Deafen = _tempDeafen;
        SettingsManager.Current.Focus  = _tempFocus;
        SettingsManager.Save();
        KeybindsSaved?.Invoke();
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
}
