using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Cordex.Core;

public static class PerformanceManager
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    public static void ApplyPerformanceSettings()
    {
        try
        {
            var settings = SettingsManager.Current;

            if (!settings.EnablePerformanceLimits)
                return;

            var process = Process.GetCurrentProcess();
            var handle = process.Handle;

            // Apply CPU affinity if enabled
            if (settings.EnableCpuAffinity && settings.CpuAffinityMask != 0)
            {
                try
                {
                    SetProcessAffinityMask(handle, new IntPtr(settings.CpuAffinityMask));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to set CPU affinity: {ex.Message}");
                }
            }
            else if (settings.MaxCpuCores > 0 && settings.MaxCpuCores < Environment.ProcessorCount)
            {
                try
                {
                    // Create affinity mask for the specified number of cores
                    long mask = 0;
                    for (int i = 0; i < settings.MaxCpuCores; i++)
                    {
                        mask |= (1L << i);
                    }
                    SetProcessAffinityMask(handle, new IntPtr(mask));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to set CPU core limit: {ex.Message}");
                }
            }

            // Apply RAM limit
            if (settings.MaxRamMB >= 100)
            {
                try
                {
                    long maxBytes = settings.MaxRamMB * 1024L * 1024L;
                    SetProcessWorkingSetSize(handle, new IntPtr(-1), new IntPtr(maxBytes));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to set RAM limit: {ex.Message}");
                }
            }

            // Set process priority if reducing background activity
            if (settings.ReduceBackgroundActivity)
            {
                try
                {
                    process.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to set process priority: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to apply performance settings: {ex.Message}");
            // Don't throw - allow app to continue even if performance settings fail
        }
    }

    public static void MonitorAndEnforceRamLimit()
    {
        var settings = SettingsManager.Current;

        if (!settings.EnablePerformanceLimits || settings.MaxRamMB < 100)
            return;

        try
        {
            var process = Process.GetCurrentProcess();
            long currentRamMB = process.WorkingSet64 / (1024 * 1024);

            if (currentRamMB > settings.MaxRamMB)
            {
                // Force garbage collection to free memory
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);

                // Trim working set
                long maxBytes = settings.MaxRamMB * 1024L * 1024L;
                SetProcessWorkingSetSize(process.Handle, new IntPtr(-1), new IntPtr(maxBytes));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enforce RAM limit: {ex.Message}");
        }
    }
}
