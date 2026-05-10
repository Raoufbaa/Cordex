using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Cordex.Core;

public class DesktopSourceProvider
{
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public string GetSources()
    {
        var list = new List<object>();
        list.Add(new { id = "screen:0:0", name = "🖥️ Entire Screen" });

        var addedHwnds = new HashSet<IntPtr>();
        var currentPid = Process.GetCurrentProcess().Id;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                // Skip our own process to prevent recursive streaming
                if (p.Id == currentPid) continue;

                if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle))
                {
                    if (IsWindowVisible(p.MainWindowHandle))
                    {
                        if (addedHwnds.Add(p.MainWindowHandle))
                        {
                            list.Add(new 
                            { 
                                id = $"window:{p.MainWindowHandle.ToInt64()}:0", 
                                name = $"🪟 {p.MainWindowTitle}" 
                            });
                        }
                    }
                }
            }
            catch
            {
                // Ignore processes we can't access
            }
        }

        return JsonSerializer.Serialize(list);
    }
}
