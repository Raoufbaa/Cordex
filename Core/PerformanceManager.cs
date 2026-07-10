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

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

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
    
    // Track if we're in an active voice connection to avoid aggressive throttling
    private static bool _isInVoiceConnection = false;
    
    // Debounce voice state changes to avoid rapid toggling
    private static DateTime _lastVoiceStateChange = DateTime.MinValue;
    private const int VOICE_STATE_DEBOUNCE_MS = 2000;
    
    public static void SetVoiceConnectionState(bool isInVoice)
    {
        // Debounce rapid state changes
        if ((DateTime.Now - _lastVoiceStateChange).TotalMilliseconds < VOICE_STATE_DEBOUNCE_MS)
            return;
            
        if (_isInVoiceConnection == isInVoice)
            return;
            
        _isInVoiceConnection = isInVoice;
        _lastVoiceStateChange = DateTime.Now;
    }

    public static bool RequiresMonitoring()
    {
        var settings = SettingsManager.Current;
        int minRamMB = Math.Max(300, settings.MaxRamMB);
        return settings.EnablePerformanceLimits &&
               (minRamMB >= 300 || settings.MaxCpuPercent < 100);
    }

    public static TimeSpan GetMonitoringInterval()
    {
        var settings = SettingsManager.Current;
        
        // If RAM limits are active, check very frequently (every 15s)
        if (settings.MaxRamMB >= 300 && settings.MaxRamMB < 500)
            return TimeSpan.FromSeconds(15);
        
        // Use 60 second interval for moderate RAM limits
        if (settings.MaxRamMB >= 500 && settings.MaxRamMB < 1000)
            return TimeSpan.FromSeconds(60);
        
        // Use 45 second interval for CPU-specific monitoring
        return settings.MaxCpuPercent < 100
            ? TimeSpan.FromSeconds(45)
            : TimeSpan.FromSeconds(90);
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

            // Enforce minimum limits
            int minCpuCores = Math.Max(2, settings.MaxCpuCores);
            int minRamMB = Math.Max(300, settings.MaxRamMB);
            
            // Update settings if they're below minimum
            if (settings.MaxCpuCores < 2)
            {
                settings.MaxCpuCores = 2;
                SettingsManager.Save();
            }
            if (settings.MaxRamMB < 300)
            {
                settings.MaxRamMB = 300;
                SettingsManager.Save();
            }

            // Apply CPU affinity if enabled - STRICT ENFORCEMENT
            if (settings.EnableCpuAffinity && settings.CpuAffinityMask != 0)
            {
                try
                {
                    // Ensure minimum 4 cores (0-3) in affinity mask
                    long minAffinityMask = 0x0F; // Binary 1111 = cores 0,1,2,3
                    long effectiveMask = settings.CpuAffinityMask | minAffinityMask;
                    SetProcessAffinityMask(handle, new IntPtr(effectiveMask));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to set CPU affinity: {ex.Message}");
                }
            }
            else if (minCpuCores > 0 && minCpuCores < Environment.ProcessorCount)
            {
                try
                {
                    // Create affinity mask for the specified number of cores (minimum 2)
                    long mask = 0;
                    for (int i = 0; i < minCpuCores; i++)
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

            // Set HARD RAM limit immediately
            if (minRamMB >= 300)
            {
                try
                {
                    long maxBytes = minRamMB * 1024L * 1024L;
                    long minBytes = 50 * 1024 * 1024; // 50MB minimum
                    SetProcessWorkingSetSize(handle, new IntPtr(minBytes), new IntPtr(maxBytes));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to set RAM limit: {ex.Message}");
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

            // Apply settings to WebView2 child processes
            ApplyWebView2ProcessLimits();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to apply performance settings: {ex.Message}");
            // Don't throw - allow app to continue even if performance settings fail
        }
    }

    private static void ApplyWebView2ProcessLimits()
    {
        var settings = SettingsManager.Current;

        if (!settings.EnablePerformanceLimits)
            return;

        try
        {
            var mainProcess = Process.GetCurrentProcess();
            var webViewProcesses = GetWebView2Processes();

            foreach (var webViewProc in webViewProcesses)
            {
                try
                {
                    if (!_trackedCefProcessIds.Contains(webViewProc.Id))
                    {
                        _trackedCefProcessIds.Add(webViewProc.Id);
                    }

                    // Apply CPU affinity - STRICT
                    int minCpuCores = Math.Max(2, settings.MaxCpuCores);
                    
                    if (settings.EnableCpuAffinity && settings.CpuAffinityMask != 0)
                    {
                        // Ensure minimum 4 cores (0-3) in affinity mask
                        long minAffinityMask = 0x0F; // Binary 1111 = cores 0,1,2,3
                        long effectiveMask = settings.CpuAffinityMask | minAffinityMask;
                        SetProcessAffinityMask(webViewProc.Handle, new IntPtr(effectiveMask));
                    }
                    else if (minCpuCores > 0 && minCpuCores < Environment.ProcessorCount)
                    {
                        long mask = 0;
                        for (int i = 0; i < minCpuCores; i++)
                        {
                            mask |= (1L << i);
                        }
                        SetProcessAffinityMask(webViewProc.Handle, new IntPtr(mask));
                    }

                    // Apply HARD RAM limit to each WebView2 process
                    int minRamMB = Math.Max(300, settings.MaxRamMB);
                    if (minRamMB >= 300)
                    {
                        // Each WebView2 process gets a portion of the total limit
                        int webViewCount = webViewProcesses.Count;
                        long totalBytes = minRamMB * 1024L * 1024L;
                        long webViewTotalBytes = (long)(totalBytes * 0.7); // 70% for all WebView2
                        long perWebViewBytes = webViewCount > 0 ? webViewTotalBytes / webViewCount : webViewTotalBytes;
                        long minBytes = 15 * 1024 * 1024; // 15MB minimum
                        
                        SetProcessWorkingSetSize(webViewProc.Handle, new IntPtr(minBytes), new IntPtr(perWebViewBytes));
                    }

                    // Set process priority
                    if (settings.ReduceBackgroundActivity)
                    {
                        webViewProc.PriorityClass = ProcessPriorityClass.BelowNormal;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to apply limits to WebView2 process {webViewProc.Id}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to apply WebView2 process limits: {ex.Message}");
        }
    }

    public static int WebView2BrowserProcessId { get; set; }

    private static List<Process> GetWebView2Processes()
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

            // 1. Clean up stale processes from cache first (fast, no system-wide scan)
            for (int i = _cachedCefProcesses.Count - 1; i >= 0; i--)
            {
                try { if (_cachedCefProcesses[i].HasExited) _cachedCefProcesses.RemoveAt(i); }
                catch { _cachedCefProcesses.RemoveAt(i); }
            }

            // 2. Rescan via snapshot only if cache is empty or 90s elapsed
            if (_cachedCefProcesses.Count == 0 || (DateTime.Now - _lastProcessScan).TotalSeconds > 90)
            {
                var mainProcess = Process.GetCurrentProcess();
                var childIds = ProcessHelper.GetChildProcessIds(mainProcess.Id);

                // Also gather child processes of our WebView2 browser process
                if (WebView2BrowserProcessId != 0)
                {
                    childIds.Add(WebView2BrowserProcessId);
                    var webViewChildren = ProcessHelper.GetChildProcessIds(WebView2BrowserProcessId);
                    foreach (var id in webViewChildren)
                    {
                        childIds.Add(id);
                    }
                }

                foreach (var id in childIds)
                {
                    try
                    {
                        if (id == mainProcess.Id) continue;
                        
                        // If already in cache, skip
                        bool alreadyTracked = false;
                        for (int j = 0; j < _cachedCefProcesses.Count; j++)
                        {
                            if (_cachedCefProcesses[j].Id == id) { alreadyTracked = true; break; }
                        }
                        if (alreadyTracked) continue;

                        var proc = Process.GetProcessById(id);
                        if (proc.ProcessName.Contains("msedgewebview2", StringComparison.OrdinalIgnoreCase))
                        {
                            _cachedCefProcesses.Add(proc);
                        }
                    }
                    catch { }
                }
                
                _lastProcessScan = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get WebView2 processes: {ex.Message}");
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
            var webViewProcesses = GetWebView2Processes();

            if (settings.MaxRamMB >= 100)
            {
                MonitorAndEnforceRamLimit(mainProcess, webViewProcesses, settings);
            }

            if (settings.MaxCpuPercent < 100)
            {
                MonitorAndEnforceCpuLimit(mainProcess, webViewProcesses, settings);
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
        int minRamMB = Math.Max(300, settings.MaxRamMB);

        if (!settings.EnablePerformanceLimits || minRamMB < 300)
            return;

        MonitorAndEnforceRamLimit(Process.GetCurrentProcess(), GetWebView2Processes(), settings);
    }

    public static void MonitorAndEnforceCpuLimit()
    {
        var settings = SettingsManager.Current;

        if (!settings.EnablePerformanceLimits || settings.MaxCpuPercent >= 100)
            return;

        MonitorAndEnforceCpuLimit(Process.GetCurrentProcess(), GetWebView2Processes(), settings);
    }

    private static void MonitorAndEnforceRamLimit(Process mainProcess, IReadOnlyList<Process> webViewProcesses, AppSettings settings)
    {
        try
        {
            // Calculate total RAM usage across all processes
            mainProcess.Refresh();
            long totalRamMB = mainProcess.WorkingSet64 / (1024 * 1024);

            foreach (var webViewProc in webViewProcesses)
            {
                try
                {
                    webViewProc.Refresh();
                    totalRamMB += webViewProc.WorkingSet64 / (1024 * 1024);
                }
                catch
                {
                    // Process may have exited
                }
            }

            // Enforce minimum RAM limit of 300MB
            int effectiveMaxRamMB = Math.Max(300, settings.MaxRamMB);
            
            // Start trimming at 80% of limit to be proactive
            long trimThresholdMB = (long)(effectiveMaxRamMB * 0.8);
            
            if (totalRamMB > trimThresholdMB)
            {
                // CRITICAL: Skip aggressive RAM trimming during active voice connections
                if (_isInVoiceConnection)
                {
                    GC.Collect(2, GCCollectionMode.Optimized, false, false);
                    return;
                }

                int processCount = webViewProcesses.Count + 1;
                long targetTotalBytes = effectiveMaxRamMB * 1024L * 1024L;
                
                // Allocate 30% to main process, 70% to WebView2 processes
                long mainTargetBytes = (long)(targetTotalBytes * 0.3);
                long webViewTotalTargetBytes = (long)(targetTotalBytes * 0.7);
                long webViewPerProcessBytes = webViewProcesses.Count > 0 ? webViewTotalTargetBytes / webViewProcesses.Count : 0;
                
                long minPerProcessBytes = 15 * 1024 * 1024; // Minimum 15MB per process

                // Non-blocking concurrent GC — avoids stop-the-world pauses
                GC.Collect(2, GCCollectionMode.Optimized, false, false);

                // Trim main process
                try
                {
                    mainProcess.Refresh();
                    long mainCurrent = mainProcess.WorkingSet64;
                    if (mainCurrent > mainTargetBytes)
                    {
                        EmptyWorkingSet(mainProcess.Handle);
                        SetProcessWorkingSetSize(mainProcess.Handle, new IntPtr(minPerProcessBytes), new IntPtr(mainTargetBytes));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to trim main process: {ex.Message}");
                }

                // Aggressively trim WebView2 processes
                foreach (var webViewProc in webViewProcesses)
                {
                    try
                    {
                        webViewProc.Refresh();
                        long currentBytes = webViewProc.WorkingSet64;
                        long webViewTargetBytes = Math.Max(minPerProcessBytes, webViewPerProcessBytes);

                        if (currentBytes > webViewTargetBytes)
                        {
                            // Empty working set then set hard limits
                            EmptyWorkingSet(webViewProc.Handle);
                            SetProcessWorkingSetSize(webViewProc.Handle, new IntPtr(minPerProcessBytes), new IntPtr(webViewTargetBytes));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to trim WebView2 process {webViewProc.Id}: {ex.Message}");
                    }
                }
            }
            else
            {
                ApplyWebView2ProcessLimits();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enforce RAM limit: {ex.Message}");
        }
    }

    private static void MonitorAndEnforceCpuLimit(Process mainProcess, IReadOnlyList<Process> webViewProcesses, AppSettings settings)
    {
        try
        {
            var allProcesses = new List<Process>(webViewProcesses.Count + 1) { mainProcess };
            allProcesses.AddRange(webViewProcesses);

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

            if (totalCpuUsage > settings.MaxCpuPercent)
            {
                double excessPercent = totalCpuUsage - settings.MaxCpuPercent;
                double throttleRatio = excessPercent / totalCpuUsage;

                foreach (var proc in allProcesses)
                {
                    try
                    {
                        if (!cpuUsageByProcess.TryGetValue(proc.Id, out double procCpuUsage) || procCpuUsage <= 1.0)
                            continue;

                        // CRITICAL: Never throttle WebView2 processes to Idle during WebRTC connections
                        // DTLS handshakes are time-sensitive and will fail if CPU scheduling is delayed
                        bool isRenderer = proc.ProcessName.Contains("msedgewebview2", StringComparison.OrdinalIgnoreCase);
                        
                        if (throttleRatio > 0.5)
                        {
                            // WebView2 processes: BelowNormal max (not Idle) to preserve WebRTC timing
                            var targetPriority = isRenderer ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Idle;
                            
                            if (proc.PriorityClass != targetPriority)
                            {
                                proc.PriorityClass = targetPriority;
                            }
                        }
                        else if (throttleRatio > 0.3)
                        {
                            if (proc.PriorityClass != ProcessPriorityClass.BelowNormal)
                            {
                                proc.PriorityClass = ProcessPriorityClass.BelowNormal;
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
