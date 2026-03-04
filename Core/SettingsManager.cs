using System;
using System.IO;
using System.Text.Json;

namespace Cordex.Core;

public class HotkeyConfig
{
    public uint   Modifiers   { get; set; }
    public uint   VirtualKey  { get; set; }
    public string Display     { get; set; } = "";
}

public class AppSettings
{
    // Keybinds
    public HotkeyConfig Mute          { get; set; } = new() { Modifiers = 0x0006, VirtualKey = 0x4D, Display = "Ctrl+Shift+M" };
    public HotkeyConfig Deafen        { get; set; } = new() { Modifiers = 0x0006, VirtualKey = 0x44, Display = "Ctrl+Shift+D" };
    public HotkeyConfig Focus         { get; set; } = new() { Modifiers = 0x0006, VirtualKey = 0x4E, Display = "Ctrl+Shift+N" };
    public HotkeyConfig PushToTalk    { get; set; } = new() { Modifiers = 0x0000, VirtualKey = 0xC0, Display = "`" };
    public HotkeyConfig PushToMute    { get; set; } = new() { Modifiers = 0x0000, VirtualKey = 0x00, Display = "Not Set" };
    
    // Window Behavior
    public bool MinimizeToTray        { get; set; } = true;
    public bool CloseToTray           { get; set; } = true;
    public bool StartMinimized        { get; set; } = false;
    public bool StartWithWindows      { get; set; } = false;
    
    // Performance
    public bool HardwareAcceleration  { get; set; } = false;
    public bool ReducedMotion         { get; set; } = false;
    
    // Voice & Audio
    public bool AutomaticallyMute     { get; set; } = false;
    public bool ShowVoiceActivity     { get; set; } = true;
    public int  VoiceActivityThreshold { get; set; } = 50;
    
    // Notifications
    public bool ShowNotifications     { get; set; } = true;
    public bool NotificationSounds    { get; set; } = true;
}

public static class SettingsManager
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cordex", "settings.json");

    public static AppSettings Current { get; private set; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
