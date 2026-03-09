using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern bool SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll")]
    private static extern int ResumeThread(IntPtr hThread);

    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_SET_INFORMATION = 0x0200;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private static readonly HashSet<int> _trackedCefProcessIds = new();
    private static int _mainProcessId;
    private static readonly Dictionary<int, TimeSpan> _lastCpuTimes = new();
    private static readonly Dictionary<int, DateTime> _lastCpuCheck = new();
    private static readonly Dictionary<int, double> _lastCpuUsage = new();

    private static List<Process> _cachedCefProcesses = new();
    private static DateTime _lastProcessScan = DateTime.MinValue;

    public static bool RequiresMonitoring()
    {
        var settings = SettingsManager.Current;
        return settings.EnablePerformanceLimits &&
               (settings.MaxRamMB >= 100 || settings.MaxCpuPercent < 100);
    }

    public static TimeSpan GetMonitoringInterval()
    {
        var settings = SettingsManager.Current;
        // Use 60 second interval when performance limits are enabled
        // Use 30 second interval for CPU-specific monitoring
        return settings.MaxCpuPercent < 100
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(60);
    }

    public static void ApplyPerformanceSettings()
    {
        try
        {
            var settings = SettingsManager.Current;

            if (!settings.EnablePerformanceLimits)
                return;

            var process = Process.GetCurrentProcess();
            _mainProcessId = process.Id;
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

            // Set soft RAM limit - Handled exclusively by CefCommandLineArgs now.
            // Using SetProcessWorkingSetSize with hard limits causes massive page thrashing and freezes.

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

            // Apply settings to CefSharp child processes
            ApplyCefSharpProcessLimits();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to apply performance settings: {ex.Message}");
            // Don't throw - allow app to continue even if performance settings fail
        }
    }

    private static void ApplyCefSharpProcessLimits()
    {
        var settings = SettingsManager.Current;

        if (!settings.EnablePerformanceLimits)
            return;

        try
        {
            var mainProcess = Process.GetCurrentProcess();
            var cefProcesses = GetCefSharpProcesses();

            foreach (var cefProc in cefProcesses)
            {
                try
                {
                    if (!_trackedCefProcessIds.Contains(cefProc.Id))
                    {
                        _trackedCefProcessIds.Add(cefProc.Id);
                        Debug.WriteLine($"Applying limits to CefSharp process: {cefProc.ProcessName} (PID: {cefProc.Id})");
                    }

                    // Apply CPU affinity
                    if (settings.EnableCpuAffinity && settings.CpuAffinityMask != 0)
                    {
                        SetProcessAffinityMask(cefProc.Handle, new IntPtr(settings.CpuAffinityMask));
                    }
                    else if (settings.MaxCpuCores > 0 && settings.MaxCpuCores < Environment.ProcessorCount)
                    {
                        long mask = 0;
                        for (int i = 0; i < settings.MaxCpuCores; i++)
                        {
                            mask |= (1L << i);
                        }
                        SetProcessAffinityMask(cefProc.Handle, new IntPtr(mask));
                    }

                    // Set soft RAM limit - Handled by CefCommandLineArgs now.

                    // Set process priority
                    if (settings.ReduceBackgroundActivity)
                    {
                        cefProc.PriorityClass = ProcessPriorityClass.BelowNormal;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to apply limits to CefSharp process {cefProc.Id}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to apply CefSharp process limits: {ex.Message}");
        }
    }

    private static List<Process> GetCefSharpProcesses()
    {
        try
        {
            // Don't scan at all if performance limits are disabled
            if (!SettingsManager.Current.EnablePerformanceLimits)
            {
                return new List<Process>();
            }

            // Clean up exited processes
            _cachedCefProcesses.RemoveAll(p => 
            {
                try { return p.HasExited; }
                catch { return true; }
            });

            // Rescan every 60 seconds or if we have no processes
            if (_cachedCefProcesses.Count == 0 || (DateTime.Now - _lastProcessScan).TotalSeconds > 60)
            {
                var mainProcess = Process.GetCurrentProcess();
                var childIds = ProcessHelper.GetChildProcessIds(mainProcess.Id);
                var allProcesses = Process.GetProcesses();

                foreach (var proc in allProcesses)
                {
                    try
                    {
                        if (proc.Id == mainProcess.Id) continue;
                        
                        if (!_cachedCefProcesses.Any(p => p.Id == proc.Id) &&
                            (childIds.Contains(proc.Id) || 
                            proc.ProcessName.Contains("CefSharp", StringComparison.OrdinalIgnoreCase) ||
                            proc.ProcessName.Contains("BrowserSubprocess", StringComparison.OrdinalIgnoreCase)))
                        {
                            _cachedCefProcesses.Add(proc);
                        }
                    }
                    catch
                    {
                        // Process may have exited or access denied
                    }
                }
                
                _lastProcessScan = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get CefSharp processes: {ex.Message}");
        }

        return _cachedCefProcesses.ToList();
    }

    public static void RunMonitoringCycle()
    {
        var settings = SettingsManager.Current;

        if (!settings.EnablePerformanceLimits)
            return;

        try
        {
            var mainProcess = Process.GetCurrentProcess();
            var cefProcesses = GetCefSharpProcesses();

            if (settings.MaxRamMB >= 100)
            {
                MonitorAndEnforceRamLimit(mainProcess, cefProcesses, settings);
            }

            if (settings.MaxCpuPercent < 100)
            {
                MonitorAndEnforceCpuLimit(mainProcess, cefProcesses, settings);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to run performance monitoring cycle: {ex.Message}");
        }
    }

    public static void MonitorAndEnforceRamLimit()
    {
        var settings = SettingsManager.Current;

        if (!settings.EnablePerformanceLimits || settings.MaxRamMB < 100)
            return;

        MonitorAndEnforceRamLimit(Process.GetCurrentProcess(), GetCefSharpProcesses(), settings);
    }

    public static void MonitorAndEnforceCpuLimit()
    {
        var settings = SettingsManager.Current;

        if (!settings.EnablePerformanceLimits || settings.MaxCpuPercent >= 100)
            return;

        MonitorAndEnforceCpuLimit(Process.GetCurrentProcess(), GetCefSharpProcesses(), settings);
    }

    private static void MonitorAndEnforceRamLimit(Process mainProcess, IReadOnlyList<Process> cefProcesses, AppSettings settings)
    {
        try
        {
            // Calculate total RAM usage across all processes
            mainProcess.Refresh();
            long totalRamMB = mainProcess.WorkingSet64 / (1024 * 1024);

            foreach (var cefProc in cefProcesses)
            {
                try
                {
                    cefProc.Refresh();
                    totalRamMB += cefProc.WorkingSet64 / (1024 * 1024);
                }
                catch
                {
                    // Process may have exited
                }
            }

            Debug.WriteLine($"Total RAM usage (Cordex + CefSharp): {totalRamMB} MB / {settings.MaxRamMB} MB");

            if (totalRamMB > settings.MaxRamMB)
            {
                Debug.WriteLine($"RAM limit exceeded by {totalRamMB - settings.MaxRamMB} MB! Enforcing limits...");

                int processCount = cefProcesses.Count + 1;
                long targetTotalBytes = settings.MaxRamMB * 1024L * 1024L;
                long targetPerProcessBytes = targetTotalBytes / processCount;

                // Only do a single optimized GC pass instead of aggressive triple collection
                GC.Collect(2, GCCollectionMode.Optimized, false, false);

                SetProcessWorkingSetSize(mainProcess.Handle, new IntPtr(1024 * 1024), new IntPtr(targetPerProcessBytes));

                foreach (var cefProc in cefProcesses)
                {
                    try
                    {
                        long currentBytes = cefProc.WorkingSet64;

                        if (currentBytes > targetPerProcessBytes)
                        {
                            Debug.WriteLine($"Trimming CefSharp process {cefProc.Id} ({cefProc.ProcessName}): {currentBytes / (1024 * 1024)} MB -> target {targetPerProcessBytes / (1024 * 1024)} MB");
                            SetProcessWorkingSetSize(cefProc.Handle, new IntPtr(1024 * 1024), new IntPtr(targetPerProcessBytes));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to trim CefSharp process {cefProc.Id}: {ex.Message}");
                    }
                }
            }
            else
            {
                ApplyCefSharpProcessLimits();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enforce RAM limit: {ex.Message}");
        }
    }

    private static void MonitorAndEnforceCpuLimit(Process mainProcess, IReadOnlyList<Process> cefProcesses, AppSettings settings)
    {
        try
        {
            var allProcesses = new List<Process>(cefProcesses.Count + 1) { mainProcess };
            allProcesses.AddRange(cefProcesses);

            double totalCpuUsage = 0;
            var cpuUsageByProcess = new Dictionary<int, double>(allProcesses.Count);

            foreach (var proc in allProcesses)
            {
                try
                {
                    double cpuUsage = GetProcessCpuUsage(proc);
                    cpuUsageByProcess[proc.Id] = cpuUsage;
                    totalCpuUsage += cpuUsage;
                }
                catch
                {
                    // Process may have exited
                }
            }

            Debug.WriteLine($"Total CPU usage (Cordex + CefSharp): {totalCpuUsage:F1}% / {settings.MaxCpuPercent}%");

            if (totalCpuUsage > settings.MaxCpuPercent)
            {
                Debug.WriteLine($"CPU limit exceeded by {totalCpuUsage - settings.MaxCpuPercent:F1}%! Throttling...");

                double excessPercent = totalCpuUsage - settings.MaxCpuPercent;
                double throttleRatio = excessPercent / totalCpuUsage;

                foreach (var proc in allProcesses)
                {
                    try
                    {
                        if (!cpuUsageByProcess.TryGetValue(proc.Id, out double procCpuUsage) || procCpuUsage <= 1.0)
                            continue;

                        if (throttleRatio > 0.5)
                        {
                            if (proc.PriorityClass != ProcessPriorityClass.Idle)
                            {
                                proc.PriorityClass = ProcessPriorityClass.Idle;
                                Debug.WriteLine($"Set idle priority for process {proc.Id} ({proc.ProcessName})");
                            }
                        }
                        else if (throttleRatio > 0.3)
                        {
                            if (proc.PriorityClass != ProcessPriorityClass.BelowNormal)
                            {
                                proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                                Debug.WriteLine($"Reduced priority for process {proc.Id} ({proc.ProcessName})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to throttle process {proc.Id}: {ex.Message}");
                    }
                }
            }
            else
            {
                foreach (var proc in allProcesses)
                {
                    try
                    {
                        if (proc.PriorityClass != ProcessPriorityClass.Normal && !settings.ReduceBackgroundActivity)
                        {
                            proc.PriorityClass = ProcessPriorityClass.Normal;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enforce CPU limit: {ex.Message}");
        }
    }

    private static double GetProcessCpuUsage(Process process)
    {
        try
        {
            int pid = process.Id;
            var currentTime = DateTime.Now;
            var currentCpuTime = process.TotalProcessorTime;

            if (!_lastCpuTimes.ContainsKey(pid) || !_lastCpuCheck.ContainsKey(pid))
            {
                _lastCpuTimes[pid] = currentCpuTime;
                _lastCpuCheck[pid] = currentTime;
                _lastCpuUsage[pid] = 0;
                return 0;
            }

            var elapsedActual = (currentTime - _lastCpuCheck[pid]).TotalMilliseconds;
            if (elapsedActual < 500)
            {
                return _lastCpuUsage.ContainsKey(pid) ? _lastCpuUsage[pid] : 0;
            }

            var elapsedCpu = (currentCpuTime - _lastCpuTimes[pid]).TotalMilliseconds;
            
            // Calculate CPU usage: (CPU time / Actual time) / ProcessorCount * 100
            double usage = (elapsedCpu / elapsedActual) / Environment.ProcessorCount * 100.0;
            
            _lastCpuTimes[pid] = currentCpuTime;
            _lastCpuCheck[pid] = currentTime;
            _lastCpuUsage[pid] = usage;

            return usage;
        }
        catch
        {
            if (_lastCpuTimes.ContainsKey(process.Id)) _lastCpuTimes.Remove(process.Id);
            return 0;
        }
    }
}
