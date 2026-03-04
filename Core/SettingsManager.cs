using System;
using System.IO;
using System.Text.Json;

namespace Nextcord.Core;

public class HotkeyConfig
{
    public uint   Modifiers   { get; set; }
    public uint   VirtualKey  { get; set; }
    public string Display     { get; set; } = "";
}

public class AppSettings
{
    public HotkeyConfig Mute   { get; set; } = new() { Modifiers = 0x0006, VirtualKey = 0x4D, Display = "Ctrl+Shift+M" };
    public HotkeyConfig Deafen { get; set; } = new() { Modifiers = 0x0006, VirtualKey = 0x44, Display = "Ctrl+Shift+D" };
    public HotkeyConfig Focus  { get; set; } = new() { Modifiers = 0x0006, VirtualKey = 0x4E, Display = "Ctrl+Shift+N" };
}

public static class SettingsManager
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Nextcord", "settings.json");

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
