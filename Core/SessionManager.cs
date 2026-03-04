using System;
using System.IO;

namespace Cordex.Core;

public static class SessionManager
{
    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cordex"
        );

    public static string CacheDirectory =>
        Path.Combine(DataDirectory, "cache");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }
}
