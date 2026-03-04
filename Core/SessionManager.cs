using System;
using System.IO;

namespace Nextcord.Core;

public static class SessionManager
{
    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Nextcord"
        );

    public static string CacheDirectory =>
        Path.Combine(DataDirectory, "cache");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }
}
