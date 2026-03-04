using System;
using System.Threading.Tasks;
using Cordex.Core;

namespace Cordex;

public class TestVersionCheck
{
    public static async Task Main()
    {
        Console.WriteLine("Testing Version Check System");
        Console.WriteLine("============================");
        Console.WriteLine();

        var currentVersion = VersionManager.GetCurrentVersion();
        Console.WriteLine($"Current Version: {currentVersion}");
        Console.WriteLine();

        Console.WriteLine("Checking version from server...");
        var result = await VersionManager.CheckVersionAsync();

        Console.WriteLine();
        Console.WriteLine("Results:");
        Console.WriteLine($"  Current Version: {result.CurrentVersion}");
        Console.WriteLine($"  Latest Version: {result.LatestVersion}");
        Console.WriteLine($"  Is Disabled: {result.IsDisabled}");
        Console.WriteLine($"  Is Supported: {result.IsSupported}");
        Console.WriteLine($"  Update Available: {result.UpdateAvailable}");
        Console.WriteLine($"  Message: {result.Message}");
        Console.WriteLine($"  Release Notes: {result.ReleaseNotes}");
        Console.WriteLine($"  Download URL: {result.DownloadUrl}");

        Console.WriteLine();
        Console.WriteLine("Expected behavior:");
        if (result.IsDisabled)
        {
            Console.WriteLine("  → App should show disabled message and exit");
        }
        else if (!result.IsSupported)
        {
            Console.WriteLine("  → App should show 'version not supported' and require update");
        }
        else if (result.UpdateAvailable)
        {
            Console.WriteLine("  → App should show optional update dialog");
        }
        else
        {
            Console.WriteLine("  → App should start normally");
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}
