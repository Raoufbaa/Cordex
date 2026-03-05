using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CefSharp;
using CefSharp.Wpf;
using Cordex.Core;

namespace Cordex;

public partial class MainWindow : Window
{
    private readonly TrayManager    _tray = new();
    private readonly KeybindManager _keys = new();
    private readonly AudioMonitor   _audio = new();

    private static bool _cefInitialized;
    private MuteState   _muteState = MuteState.Default;
    private bool        _isExiting = false;
    private bool        _isInVoiceChannel = false;
    private bool        _discordLoaded = false;
    private System.Threading.Timer? _voiceCheckTimer;
    
    // Keep icon handles alive
    private System.Drawing.Icon? _bigIcon;
    private System.Drawing.Icon? _smallIcon;
    
    private static readonly string IconPath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");

    // ── Win32 constants ──────────────────────────────────────────────────────
    private const int    WM_NCHITTEST     = 0x0084;
    private const int    WM_GETMINMAXINFO = 0x0024;
    private const int    WM_SETICON       = 0x0080;
    private const int    ICON_SMALL       = 0;
    private const int    ICON_BIG         = 1;
    private const int    HTCAPTION        = 2;
    private const int    HTLEFT           = 10;
    private const int    HTRIGHT          = 11;
    private const int    HTTOP            = 12;
    private const int    HTTOPLEFT        = 13;
    private const int    HTTOPRIGHT       = 14;
    private const int    HTBOTTOM         = 15;
    private const int    HTBOTTOMLEFT     = 16;
    private const int    HTBOTTOMRIGHT    = 17;
    private const int    GWL_EXSTYLE      = -20;
    private const int    WS_EX_APPWINDOW  = 0x00040000;
    private const int    WS_EX_TOOLWINDOW = 0x00000080;
    private const uint   MONITOR_NEAREST  = 0x00000002;
    private const uint   ABM_GETSTATE     = 0x00000004;
    private const int    ABS_AUTOHIDE     = 0x00000001;
    private const uint   SWP_NOMOVE       = 0x0002;
    private const uint   SWP_NOSIZE       = 0x0001;
    private const uint   SWP_NOZORDER     = 0x0004;
    private const uint   SWP_FRAMECHANGED = 0x0020;
    private const int    SW_HIDE          = 0;
    private const int    SW_SHOW          = 5;
    private const uint   DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int    DWMWCP_ROUND     = 2;
    private const int    DWMWCP_DONOTROUND = 1;

    private const double ResizeBorder = 8;
    private const double DragHeight   = 36;
    private const double LeftReserve  = 130;
    private const double RightReserve = 80;

    private static readonly System.Windows.Media.SolidColorBrush BorderColor =
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3A3C40"));

    // ── Win32 structs ────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int  cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int    cbSize;
        public IntPtr hWnd;
        public uint   uCallbackMessage;
        public uint   uEdge;
        public RECT   rc;
        public IntPtr lParam;
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────────
    [DllImport("user32.dll")]  private static extern int    GetWindowLong        (IntPtr hwnd, int idx);
    [DllImport("user32.dll")]  private static extern int    SetWindowLong        (IntPtr hwnd, int idx, int val);
    [DllImport("user32.dll")]  private static extern IntPtr MonitorFromWindow    (IntPtr hwnd, uint flags);
    [DllImport("user32.dll")]  private static extern bool   GetMonitorInfo       (IntPtr hMon, ref MONITORINFO mi);
    [DllImport("user32.dll")]  private static extern IntPtr SendMessage          (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage      (uint msg, ref APPBARDATA data);
    [DllImport("dwmapi.dll")]  private static extern int    DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref int attrValue, int attrSize);
    [DllImport("user32.dll")]  private static extern bool   SetWindowPos         (IntPtr hwnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]  private static extern bool   ShowWindow           (IntPtr hwnd, int nCmdShow);

    // ── Constructor ──────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeCef();
        InitializeComponent();

        var area = SystemParameters.WorkArea;
        Width  = area.Width  * 0.85;
        Height = area.Height * 0.85;

        // Load icon early
        if (System.IO.File.Exists(IconPath))
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(IconPath, UriKind.Absolute));
        }

        Browser.RequestHandler  = new DiscordRequestHandler();
        Browser.MenuHandler     = new NoContextMenuHandler();
        Browser.PermissionHandler = new MediaPermissionHandler();
        Browser.LifeSpanHandler = new ScreenShareLifeSpanHandler();
        Browser.BrowserSettings = new BrowserSettings { WindowlessFrameRate = 60 };
        Browser.Address         = "https://discord.com/app";
        Browser.LoadingStateChanged += OnBrowserLoadingStateChanged;

        Loaded            += OnWindowLoaded;
        SourceInitialized += OnSourceInitialized;
        StateChanged      += OnStateChanged;
    }

    // ── Win32 Hook ───────────────────────────────────────────────────────────
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // Load icon FIRST before any window style changes
        try
        {
            if (System.IO.File.Exists(IconPath))
            {
                _bigIcon   = new System.Drawing.Icon(IconPath, 32, 32);
                _smallIcon = new System.Drawing.Icon(IconPath, 16, 16);
                SendMessage(hwnd, WM_SETICON, new IntPtr(ICON_BIG),   _bigIcon.Handle);
                SendMessage(hwnd, WM_SETICON, new IntPtr(ICON_SMALL), _smallIcon.Handle);
            }
        }
        catch { }

        // Set extended window style to force taskbar button and app classification
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_APPWINDOW;   // Force taskbar button and Task Manager "Apps" classification
        exStyle &= ~WS_EX_TOOLWINDOW; // Remove tool window flag
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        
        // Force window frame to update
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

        // Windows 11 rounded corners
        int pref = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        if (WindowState == WindowState.Maximized)
        {
            int pref = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            WindowBorder.BorderBrush  = System.Windows.Media.Brushes.Transparent;
            WindowBorder.CornerRadius = new CornerRadius(0);
        }
        else
        {
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            WindowBorder.BorderBrush  = BorderColor;
            WindowBorder.CornerRadius = new CornerRadius(8);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // ── Maximized bounds + auto-hide taskbar ──────────────────────────
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi     = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var monitor = MonitorFromWindow(hwnd, MONITOR_NEAREST);
            var mi      = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref mi);

            mmi.ptMaxPosition.X = mi.rcWork.Left   - mi.rcMonitor.Left;
            mmi.ptMaxPosition.Y = mi.rcWork.Top    - mi.rcMonitor.Top;
            mmi.ptMaxSize.X     = mi.rcWork.Right  - mi.rcWork.Left;
            mmi.ptMaxSize.Y     = mi.rcWork.Bottom - mi.rcWork.Top;

            var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            if ((SHAppBarMessage(ABM_GETSTATE, ref abd).ToInt32() & ABS_AUTOHIDE) != 0)
                mmi.ptMaxSize.Y -= 1;

            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
            return IntPtr.Zero;
        }

        // ── Resize handles + drag zone ────────────────────────────────────
        if (msg == WM_NCHITTEST)
        {
            int sx = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            int sy = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

            var pt = PointFromScreen(new System.Windows.Point(sx, sy));
            double x = pt.X, y = pt.Y, w = ActualWidth, h = ActualHeight;

            bool L = x < ResizeBorder, R = x > w - ResizeBorder;
            bool T = y < ResizeBorder, B = y > h - ResizeBorder;

            if (WindowState != WindowState.Maximized)
            {
                if (T && L) { handled = true; return new IntPtr(HTTOPLEFT);     }
                if (T && R) { handled = true; return new IntPtr(HTTOPRIGHT);    }
                if (B && L) { handled = true; return new IntPtr(HTBOTTOMLEFT);  }
                if (B && R) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
                if (L)      { handled = true; return new IntPtr(HTLEFT);        }
                if (R)      { handled = true; return new IntPtr(HTRIGHT);       }
                if (T)      { handled = true; return new IntPtr(HTTOP);         }
                if (B)      { handled = true; return new IntPtr(HTBOTTOM);      }
            }

            if (y >= 0 && y <= DragHeight && x > LeftReserve && x < w - RightReserve)
            {
                handled = true;
                return new IntPtr(HTCAPTION);
            }
        }

        return IntPtr.Zero;
    }

    // ── CefSharp Init ────────────────────────────────────────────────────────
    private static void InitializeCef()
    {
        if (_cefInitialized) return;
        SessionManager.EnsureDirectories();

        var settings = new CefSettings
        {
            CachePath   = SessionManager.CacheDirectory,
            LogSeverity = LogSeverity.Disable,
            UserAgent   = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            WindowlessRenderingEnabled = false
        };

        // Check if hardware acceleration is enabled
        bool hwAccel = SettingsManager.Current.HardwareAcceleration;

        // Essential media stream flags
        settings.CefCommandLineArgs.Add("enable-media-stream", "1");
        settings.CefCommandLineArgs.Add("enable-usermedia-screen-capturing", "1");
        // Enable legacy screen capture API for CEF (chromeMediaSource)
        settings.CefCommandLineArgs.Add("enable-usermedia-screen-capture", "1");
        // Use fake UI to auto-approve screen capture (we show our own picker)
        settings.CefCommandLineArgs.Add("use-fake-ui-for-media-stream", "1");
        // Auto-select the entire screen as the default source
        settings.CefCommandLineArgs.Add("auto-select-desktop-capture-source", "Entire screen");
        settings.CefCommandLineArgs.Add("autoplay-policy", "no-user-gesture-required");

        // NOTE: WebRTC IP handling (all_interfaces, multiple_routes, nonproxied_udp)
        // must be set as RequestContext preferences — NOT command-line args.
        // They are applied after CEF init in ApplyWebRtcRequestContextPreferences().
        // Passing them as CLI args has no effect in CefSharp and can cause conflicts.
        settings.CefCommandLineArgs.Add("enforce-webrtc-ip-permission-check", "0");

        // Disable mDNS obfuscation of local IPs in ICE candidates.
        // Chrome's WebRtcHideLocalIpsWithMdns feature replaces real IPs with .local hostnames.
        // CefSharp has NO mDNS resolver, so these candidates are unresolvable — ICE silently
        // hangs at 'checking' forever (never reaches 'failed', bypassing our error handlers).
        // (Combined with other disable-features below)

        // Remove the renderer process sandbox so CefSharp's renderer can create UDP sockets
        // freely for WebRTC ICE connectivity checks. Without this, the sandbox may prevent
        // the renderer from binding UDP ports, causing ICE to silently fail on some systems.
        settings.CefCommandLineArgs.Add("no-sandbox", "1");

        // Performance settings based on hardware acceleration
        if (hwAccel)
        {
            settings.CefCommandLineArgs.Add("enable-gpu", "1");
            settings.CefCommandLineArgs.Add("enable-gpu-rasterization", "1");
            settings.CefCommandLineArgs.Add("enable-accelerated-video-decode", "1");
        }
        else
        {
            settings.CefCommandLineArgs.Add("disable-gpu", "1");
            settings.CefCommandLineArgs.Add("disable-gpu-compositing", "1");
            settings.CefCommandLineArgs.Add("disable-accelerated-video-decode", "1");
        }

        settings.CefCommandLineArgs.Add("disable-renderer-backgrounding", "1");
        settings.CefCommandLineArgs.Add("disable-background-timer-throttling", "1");
        settings.CefCommandLineArgs.Add("disable-backgrounding-occluded-windows", "1");

        // Aggressive Performance Flags
        settings.CefCommandLineArgs.Add("disable-site-isolation-trials", "1");       // Huge RAM/CPU saver for single-site wrappers
        settings.CefCommandLineArgs.Add("enable-quic", "1");                         // Faster networking
        settings.CefCommandLineArgs.Add("disable-extensions", "1");                  // Disable extension system entirely
        settings.CefCommandLineArgs.Add("disable-pdf-extension", "1");
        settings.CefCommandLineArgs.Add("disable-plugins-discovery", "1");
        settings.CefCommandLineArgs.Add("disable-spell-checking", "1");              // Saves CPU on typing
        settings.CefCommandLineArgs.Add("disable-print-preview", "1");               // Not needed in Discord wrapper
        settings.CefCommandLineArgs.Add("disable-reading-from-canvas", "1");         // Anti-fingerprinting but performance positive
        settings.CefCommandLineArgs.Add("enable-zero-copy", "1");                    // Reduces CPU usage during rasterization (works well with hardware accel)
        settings.CefCommandLineArgs.Add("disable-component-update", "1");            // Stops background component updates
        settings.CefCommandLineArgs.Add("disable-features", "WebRtcHideLocalIpsWithMdns,InterestFeedContentSuggestions,BlinkGenPropertyTrees,SafeBrowsing"); // Combine all disable-features here
        settings.CefCommandLineArgs.Add("disable-logging", "1");
        settings.CefCommandLineArgs.Add("disable-metrics", "1");
        settings.CefCommandLineArgs.Add("disable-metrics-reporter", "1");

        // Apply performance limits if enabled
        if (SettingsManager.Current.EnablePerformanceLimits)
        {
            // Memory limits
            if (SettingsManager.Current.MaxRamMB >= 100)
            {
                int maxRamMB = SettingsManager.Current.MaxRamMB;
                
                // Set renderer process limit (in MB)
                settings.CefCommandLineArgs.Add("renderer-process-limit", "3");
                
                // Limit JavaScript heap size - scale with total RAM limit
                // Give JS heap about 40% of total limit divided by expected processes
                int jsHeapLimit = (maxRamMB * 40) / 100; // 40% of total for JS heap
                settings.CefCommandLineArgs.Add("max-old-space-size", jsHeapLimit.ToString());
                
                // Limit disk cache size - scale with RAM limit
                long diskCacheSize = Math.Min(100 * 1024 * 1024, maxRamMB * 1024L * 1024L / 2); // 100MB max or half of RAM limit
                settings.CefCommandLineArgs.Add("disk-cache-size", diskCacheSize.ToString());
                
                // Reduce media cache - scale with RAM limit
                long mediaCacheSize = Math.Min(50 * 1024 * 1024, maxRamMB * 1024L * 512L); // 50MB max
                settings.CefCommandLineArgs.Add("media-cache-size", mediaCacheSize.ToString());
            }

            // CPU optimization flags
            // Reduce animation frame rate to save CPU
            int targetFps = 30; // Aggressively force 30 FPS if limits enabled
            settings.CefCommandLineArgs.Add("max-gum-fps", targetFps.ToString());
            
            // Limit background tab CPU usage
            settings.CefCommandLineArgs.Add("disable-background-networking", "1");
            settings.CefCommandLineArgs.Add("disable-sync", "1");
        }

        Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null);
        _cefInitialized = true;

        // Apply WebRTC routing preferences via RequestContext (the only way that works in CefSharp)
        ApplyWebRtcRequestContextPreferences();
    }

    // Sets WebRTC IP routing preferences on the CEF UI thread via RequestContext.
    // This is the only reliable method in CefSharp — command-line args have no effect
    // for these specific WebRTC preferences.
    private static void ApplyWebRtcRequestContextPreferences()
    {
        _ = Cef.UIThreadTaskFactory.StartNew(() =>
        {
            var ctx = Cef.GetGlobalRequestContext();
            if (ctx == null) return;

            // All interfaces: allows Discord to enumerate all local IPs for ICE candidates
            ctx.SetPreference("webrtc.ip_handling_policy", "all_interfaces", out _);
            // Allow multiple ICE routes (host, srflx, relay) simultaneously
            ctx.SetPreference("webrtc.multiple_routes_enabled", true, out _);
            // Allow UDP even when a proxy is configured (critical for DTLS handshake)
            ctx.SetPreference("webrtc.nonproxied_udp_enabled", true, out _);
        });
    }

    // ── Startup ──────────────────────────────────────────────────────────────
    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        InitTray();
        // Don't init keybinds yet - wait for Discord to load
        
        // Start checking for voice channel connection
        _voiceCheckTimer = new System.Threading.Timer(CheckVoiceChannelStatus, null, 2000, 2000);
    }

    private void InitTray()
    {
        _tray.Initialize();
        _tray.OpenRequested += ShowApp;
        _tray.MuteToggled   += ToggleMute;
        _tray.ExitRequested += ExitApp;
        
        _audio.VoiceActivityChanged += OnVoiceActivityChanged;
    }

    private void InitKeybinds()
    {
        _keys.Register(this);
        _keys.ToggleMute   += ToggleMute;
        _keys.ToggleDeafen += ToggleDeafen;
        _keys.FocusWindow  += ShowApp;
    }

    private void OnBrowserLoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (!e.IsLoading)
        {
            if (!_discordLoaded)
            {
                _discordLoaded = true;
                Dispatcher.Invoke(() =>
                {
                    // Wait a bit more for Discord to fully initialize before keybinds
                    System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            InitKeybinds();

                            // Apply reduced motion CSS if enabled
                            if (SettingsManager.Current.ReducedMotion)
                                ApplyReducedMotion();
                        });
                    });
                });
            }

            // Inject screen share picker script first
            Dispatcher.Invoke(InjectScreenSharePicker);
            
            // Inject WebRTC recovery script on every page load.
            // ROOT CAUSE OF DTLS HANG:
            //   When Discord disconnects from a voice channel, it calls getCodecSurvey()
            //   which throws "getCodecSurvey is not implemented on MediaEngine of browsers".
            //   This unhandled error corrupts CefSharp's WebRTC WASM state — UDP ports
            //   from the previous session remain locked, so the next DTLS handshake hangs.
            // FIX: Catch the error + any RTCPeerConnection failure, then reload the page
            //   to reset the WebRTC sandbox, giving every voice join a clean slate.
            Dispatcher.Invoke(InjectWebRtcRecoveryScript);
        }
    }

    private void InjectScreenSharePicker()
    {
        const string script = @"
(function() {
    if (window.__cordexScreenShareFixed) return;
    window.__cordexScreenShareFixed = true;

    console.log('[Cordex] Installing screen share handler for CEF');

    // Store original
    const _origGetDisplayMedia = navigator.mediaDevices.getDisplayMedia.bind(navigator.mediaDevices);
    let _isScreenShareActive = false;
    let _currentStream = null;
    
    // Create a simple picker UI
    function showScreenPicker() {
        return new Promise((resolve, reject) => {
            const overlay = document.createElement('div');
            overlay.style.cssText = `
                position: fixed;
                top: 0;
                left: 0;
                width: 100%;
                height: 100%;
                background: rgba(0, 0, 0, 0.85);
                z-index: 999999;
                display: flex;
                align-items: center;
                justify-content: center;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            `;
            
            const dialog = document.createElement('div');
            dialog.style.cssText = `
                background: #2b2d31;
                border-radius: 8px;
                padding: 24px;
                min-width: 400px;
                max-width: 500px;
                color: #fff;
                box-shadow: 0 8px 16px rgba(0,0,0,0.4);
            `;
            
            dialog.innerHTML = `
                <h2 style='margin: 0 0 16px 0; font-size: 20px; font-weight: 600;'>Choose what to share</h2>
                <div style='margin-bottom: 20px;'>
                    <button id='cordex-share-screen' style='
                        width: 100%;
                        padding: 16px;
                        margin-bottom: 12px;
                        background: #5865f2;
                        color: white;
                        border: none;
                        border-radius: 4px;
                        font-size: 16px;
                        cursor: pointer;
                        font-weight: 500;
                    '>🖥️ Entire Screen</button>
                    <button id='cordex-share-window' style='
                        width: 100%;
                        padding: 16px;
                        background: #4752c4;
                        color: white;
                        border: none;
                        border-radius: 4px;
                        font-size: 16px;
                        cursor: pointer;
                        font-weight: 500;
                    '>🪟 Application Window</button>
                </div>
                <button id='cordex-cancel' style='
                    width: 100%;
                    padding: 12px;
                    background: transparent;
                    color: #b9bbbe;
                    border: 1px solid #4e5058;
                    border-radius: 4px;
                    font-size: 14px;
                    cursor: pointer;
                '>Cancel</button>
            `;
            
            overlay.appendChild(dialog);
            document.body.appendChild(overlay);
            
            const buttons = dialog.querySelectorAll('button');
            buttons.forEach(btn => {
                btn.addEventListener('mouseenter', () => {
                    if (btn.id !== 'cordex-cancel') {
                        btn.style.filter = 'brightness(1.1)';
                    } else {
                        btn.style.background = '#4e5058';
                    }
                });
                btn.addEventListener('mouseleave', () => {
                    btn.style.filter = '';
                    if (btn.id === 'cordex-cancel') {
                        btn.style.background = 'transparent';
                    }
                });
            });
            
            document.getElementById('cordex-share-screen').onclick = () => {
                document.body.removeChild(overlay);
                resolve('screen');
            };
            
            document.getElementById('cordex-share-window').onclick = () => {
                document.body.removeChild(overlay);
                resolve('window');
            };
            
            document.getElementById('cordex-cancel').onclick = () => {
                document.body.removeChild(overlay);
                reject(new DOMException('User cancelled screen share', 'NotAllowedError'));
            };
        });
    }
    
    // Override getDisplayMedia
    navigator.mediaDevices.getDisplayMedia = async function(constraints) {
        console.log('[Cordex] getDisplayMedia intercepted, original constraints:', JSON.stringify(constraints));
        
        // If screen share is already active, stop the previous stream first
        if (_isScreenShareActive && _currentStream) {
            console.log('[Cordex] Stopping previous screen share stream before starting new one');
            _currentStream.getTracks().forEach(track => track.stop());
            _currentStream = null;
            _isScreenShareActive = false;
            // Wait a bit for cleanup
            await new Promise(resolve => setTimeout(resolve, 500));
        }
        
        try {
            // Show our picker
            const choice = await showScreenPicker();
            console.log('[Cordex] User selected:', choice);
            
            // Build constraints based on user choice
            const modifiedConstraints = {
                video: {
                    displaySurface: choice === 'screen' ? 'monitor' : 'window',
                    cursor: 'always',
                    width: { ideal: 1920, max: 3840 },
                    height: { ideal: 1080, max: 2160 },
                    frameRate: { ideal: 30, max: 60 }
                },
                audio: constraints?.audio || false
            };
            
            console.log('[Cordex] Calling original getDisplayMedia with:', JSON.stringify(modifiedConstraints));
            
            // Call the original getDisplayMedia - CEF should auto-approve with fake UI
            const stream = await _origGetDisplayMedia(modifiedConstraints);
            
            _currentStream = stream;
            _isScreenShareActive = true;
            
            // Monitor when stream ends
            stream.getTracks().forEach(track => {
                track.addEventListener('ended', () => {
                    console.log('[Cordex] Screen share track ended');
                    _isScreenShareActive = false;
                    _currentStream = null;
                });
            });
            
            console.log('[Cordex] Stream obtained successfully!');
            console.log('[Cordex] Stream ID:', stream.id);
            console.log('[Cordex] Video tracks:', stream.getVideoTracks().length);
            console.log('[Cordex] Audio tracks:', stream.getAudioTracks().length);
            
            if (stream.getVideoTracks().length > 0) {
                const videoTrack = stream.getVideoTracks()[0];
                const settings = videoTrack.getSettings();
                console.log('[Cordex] Video track settings:', JSON.stringify(settings));
                console.log('[Cordex] Video track enabled:', videoTrack.enabled);
                console.log('[Cordex] Video track muted:', videoTrack.muted);
                console.log('[Cordex] Video track readyState:', videoTrack.readyState);
            }
            
            return stream;
            
        } catch (error) {
            _isScreenShareActive = false;
            _currentStream = null;
            console.error('[Cordex] Screen share failed!');
            console.error('[Cordex] Error name:', error.name);
            console.error('[Cordex] Error message:', error.message);
            console.error('[Cordex] Error stack:', error.stack);
            throw error;
        }
    };
    
    console.log('[Cordex] Screen share handler installed successfully');
})();
";
        ExecuteScript(script);
    }

    private void InjectWebRtcRecoveryScript()
    {
        const string script = @"
(function() {
    if (window.__cordexWebRtcRecovery) return;
    window.__cordexWebRtcRecovery = true;

    var _actionPending   = false;
    var _reconnectCount  = 0;
    var _isScreenSharing = false;  // Track if we're currently screen sharing
    var MAX_SOFT_RETRIES = 2;

    // Track screen sharing state
    if (navigator.mediaDevices && navigator.mediaDevices.getDisplayMedia) {
        const _origGetDisplayMedia = navigator.mediaDevices.getDisplayMedia;
        navigator.mediaDevices.getDisplayMedia = async function() {
            _isScreenSharing = true;
            try {
                const stream = await _origGetDisplayMedia.apply(this, arguments);
                // Monitor when screen share stops
                stream.getTracks().forEach(track => {
                    track.addEventListener('ended', () => {
                        _isScreenSharing = false;
                        console.log('[Cordex] Screen share track ended');
                    });
                });
                return stream;
            } catch (error) {
                _isScreenSharing = false;
                throw error;
            }
        };
    }

    function scheduleReload(reason) {
        if (_actionPending) return;
        // Don't reload if we're actively screen sharing
        if (_isScreenSharing) {
            console.warn('[Cordex] Skipping reload - screen share is active (' + reason + ')');
            return;
        }
        _actionPending = true;
        console.warn('[Cordex] Hard reload (' + reason + ')...');
        setTimeout(function() { location.reload(); }, 800);
    }

    function attemptSmartReconnect(reason) {
        if (_actionPending) return;
        // Don't reconnect if we're actively screen sharing
        if (_isScreenSharing) {
            console.warn('[Cordex] Skipping reconnect - screen share is active (' + reason + ')');
            return;
        }
        _actionPending = true;
        _reconnectCount++;

        if (_reconnectCount > MAX_SOFT_RETRIES) {
            console.warn('[Cordex] Max reconnects reached, hard reload...');
            setTimeout(function() { location.reload(); }, 500);
            return;
        }

        console.warn('[Cordex] DTLS stuck (' + reason + '). Soft reconnect #' + _reconnectCount + '...');
        var channelPath = window.location.pathname;

        setTimeout(function() {
            var retryBtn = document.querySelector('[aria-label=""Reconnect""]') ||
                           document.querySelector('[aria-label=""Try Again""]');
            if (retryBtn) {
                console.warn('[Cordex] Clicking Discord Reconnect button.');
                retryBtn.click();
                setTimeout(function() { _actionPending = false; }, 8000);
                return;
            }

            var disconnectBtn = document.querySelector('[aria-label=""Disconnect""]');
            if (disconnectBtn) {
                console.warn('[Cordex] Disconnecting from broken voice session...');
                disconnectBtn.click();

                setTimeout(function() {
                    try {
                        window.history.pushState({}, '', channelPath);
                        window.dispatchEvent(new PopStateEvent('popstate', { state: {} }));
                    } catch(ex) {}

                    setTimeout(function() {
                        var joinBtn = document.querySelector('[aria-label=""Join Voice Channel""]') ||
                                      document.querySelector('[class*=""joinButton""]') ||
                                      document.querySelector('button[class*=""connect""]');
                        if (joinBtn) {
                            console.warn('[Cordex] Auto-clicking Join Voice Channel.');
                            joinBtn.click();
                        }
                        setTimeout(function() { _actionPending = false; }, 8000);
                    }, 1500);
                }, 1500);
            } else {
                location.reload();
            }
        }, 800);
    }

    // getCodecSurvey error handler - don't reload if screen sharing
    window.addEventListener('unhandledrejection', function(e) {
        var msg = (e && e.reason && e.reason.message) ? e.reason.message : '';
        if (msg.indexOf('getCodecSurvey') !== -1 || msg.indexOf('MediaEngine') !== -1) {
            e.preventDefault();
            if (!_isScreenSharing) {
                scheduleReload('getCodecSurvey unhandledrejection');
            } else {
                console.warn('[Cordex] getCodecSurvey error suppressed - screen share active');
            }
        }
    });
    window.addEventListener('error', function(e) {
        var msg = (e && e.message) ? e.message : '';
        if (msg.indexOf('getCodecSurvey') !== -1 || msg.indexOf('MediaEngine') !== -1) {
            e.preventDefault();
            if (!_isScreenSharing) {
                scheduleReload('getCodecSurvey error');
            } else {
                console.warn('[Cordex] getCodecSurvey error suppressed - screen share active');
            }
        }
    });

    var _OrigRTCPeerConnection = window.RTCPeerConnection;
    if (_OrigRTCPeerConnection) {
        window.RTCPeerConnection = function(config, constraints) {
            var pc  = new _OrigRTCPeerConnection(config, constraints);
            var _iceTimeout   = null;
            var ICE_TIMEOUT_MS = 18000;

            var _origCreateOffer    = pc.createOffer.bind(pc);
            var _offerTimer         = null;
            var _pendingRes         = null;
            var _pendingRej         = null;
            pc.createOffer = function(optOrSucc, fail, opts) {
                var o = (typeof optOrSucc === 'object' && optOrSucc !== null) ? optOrSucc : (opts || {});
                if (typeof optOrSucc === 'function') return _origCreateOffer(optOrSucc, fail, o);
                return new Promise(function(res, rej) {
                    if (_offerTimer !== null) {
                        clearTimeout(_offerTimer);
                        if (_pendingRej) _pendingRej(new DOMException('debounced', 'InvalidStateError'));
                    }
                    _pendingRes = res; _pendingRej = rej;
                    _offerTimer = setTimeout(function() {
                        _offerTimer = _pendingRes = _pendingRej = null;
                        _origCreateOffer(o).then(res, rej);
                    }, 400);
                });
            };

            function clearIceTimeout() {
                if (_iceTimeout !== null) { clearTimeout(_iceTimeout); _iceTimeout = null; }
            }
            function startIceTimeout(label) {
                clearIceTimeout();
                _iceTimeout = setTimeout(function() {
                    if (pc.iceConnectionState === 'checking' || pc.iceConnectionState === 'new' ||
                        pc.connectionState    === 'connecting') {
                        attemptSmartReconnect('ICE stuck after 18s (' + label + ')');
                    }
                }, ICE_TIMEOUT_MS);
            }

            pc.addEventListener('connectionstatechange', function() {
                var s = pc.connectionState;
                if      (s === 'failed')     { clearIceTimeout(); attemptSmartReconnect('connectionState=failed'); }
                else if (s === 'connecting') { startIceTimeout('connectionState=connecting'); }
                else if (s === 'connected' || s === 'closed') { clearIceTimeout(); _reconnectCount = 0; }
            });
            pc.addEventListener('iceconnectionstatechange', function() {
                var s = pc.iceConnectionState;
                if      (s === 'failed')    { clearIceTimeout(); attemptSmartReconnect('iceConnectionState=failed'); }
                else if (s === 'checking')  { startIceTimeout('iceConnectionState=checking'); }
                else if (s === 'connected' || s === 'completed' || s === 'closed') { clearIceTimeout(); }
            });

            return pc;
        };
        window.RTCPeerConnection.prototype = _OrigRTCPeerConnection.prototype;
    }
})();
";
        ExecuteScript(script);
    }


    private void ApplyReducedMotion()
    {
        var css = @"
            * {
                animation-duration: 0.01ms !important;
                animation-iteration-count: 1 !important;
                transition-duration: 0.01ms !important;
            }
        ";
        
        var script = $@"
            (function() {{
                var style = document.createElement('style');
                style.textContent = `{css}`;
                document.head.appendChild(style);
            }})();
        ";
        
        ExecuteScript(script);
    }

    private void CheckVoiceChannelStatus(object? state)
    {
        if (!_discordLoaded) return;

        Dispatcher.Invoke(() =>
        {
            ExecuteScriptAsync(@"
                (function() {
                    const voicePanel = document.querySelector('[class*=""panels""] [class*=""container""]');
                    const voiceButtons = document.querySelectorAll('[aria-label=""Disconnect""], [aria-label=""Mute""], [aria-label=""Unmute""]');
                    return voiceButtons.length > 0;
                })();
            ", (result) =>
            {
                bool inVoice = result is bool b && b;
                
                if (inVoice != _isInVoiceChannel)
                {
                    bool wasInVoice = _isInVoiceChannel;
                    _isInVoiceChannel = inVoice;
                    
                    if (!_isInVoiceChannel)
                    {
                        // Not in voice - show default icon and stop audio monitoring
                        _muteState = MuteState.Default;
                        _tray.SetState(MuteState.Default);
                        _audio.Stop();
                    }
                    else
                    {
                        // Just joined voice channel
                        if (!wasInVoice && SettingsManager.Current.AutomaticallyMute)
                        {
                            // Auto-mute on join
                            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    ExecuteScript(@"(function(){var b=document.querySelector('[aria-label=""Mute""]');if(b)b.click();})();");
                                    _muteState = MuteState.Muted;
                                    _tray.SetState(MuteState.Muted);
                                    _audio.Stop();
                                });
                            });
                        }
                        else
                        {
                            // Check current mute state
                            CheckMuteState();
                        }
                    }
                }
            });
        });
    }

    private void CheckMuteState()
    {
        ExecuteScriptAsync(@"
            (function() {
                const muteBtn = document.querySelector('[aria-label=""Unmute""]');
                return muteBtn !== null;
            })();
        ", (result) =>
        {
            bool isMuted = result is bool b && b;
            _muteState = isMuted ? MuteState.Muted : MuteState.Unmuted;
            _tray.SetState(_muteState);
            
            // Only show voice activity if enabled in settings
            if (SettingsManager.Current.ShowVoiceActivity)
            {
                if (_muteState == MuteState.Unmuted)
                    _audio.Start();
                else
                    _audio.Stop();
            }
            else
            {
                _audio.Stop();
            }
        });
    }

    private void ExecuteScriptAsync(string script, Action<object?> callback)
    {
        Browser.GetMainFrame()?.EvaluateScriptAsync(script).ContinueWith(task =>
        {
            if (task.IsCompleted && task.Result.Success)
            {
                Dispatcher.Invoke(() => callback(task.Result.Result));
            }
        });
    }

    // ── DevTools (F9) ────────────────────────────────────────────────────────
    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F9)
        {
            Browser.ShowDevTools();
            e.Handled = true;
        }
    }

    // ── Title Bar Buttons ─────────────────────────────────────────────────────
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsManager.Current.CloseToTray)
            Hide();
        else
            ExitApp();
    }
    
    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsManager.Current.MinimizeToTray)
            Hide();
        else
            WindowState = WindowState.Minimized;
    }
    
    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        win.KeybindsSaved += ReloadKeybinds;
        win.SettingsChanged += OnSettingsChanged;
        win.ShowDialog();
    }

    private void OnSettingsChanged()
    {
        // Reload voice activity monitoring based on new settings
        if (_isInVoiceChannel)
        {
            if (SettingsManager.Current.ShowVoiceActivity && _muteState == MuteState.Unmuted)
            {
                _audio.Start();
            }
            else
            {
                _audio.Stop();
            }
        }
    }

    // ── Actions ──────────────────────────────────────────────────────────────
    private void ExecuteScript(string script)
    {
        Browser.GetMainFrame()?.ExecuteJavaScriptAsync(script);
    }

    private void ToggleMute()
    {
        if (!_isInVoiceChannel) return; // Don't toggle if not in voice
        
        ExecuteScript(@"(function(){var b=document.querySelector('[aria-label=""Mute""],[aria-label=""Unmute""]');if(b)b.click();})();");
        _muteState = _muteState == MuteState.Muted ? MuteState.Unmuted : MuteState.Muted;
        _tray.SetState(_muteState);
        
        // Start/stop audio monitoring based on mute state and settings
        if (SettingsManager.Current.ShowVoiceActivity)
        {
            if (_muteState == MuteState.Unmuted)
                _audio.Start();
            else
                _audio.Stop();
        }
        else
        {
            _audio.Stop();
        }
    }

    private void ToggleDeafen()
        => ExecuteScript(@"(function(){var b=document.querySelector('[aria-label=""Deafen""],[aria-label=""Undeafen""]');if(b)b.click();})();");

    private void ShowApp()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Focus();
        });
    }

    private void ReloadKeybinds() { _keys.Unregister(); _keys.Register(this); }

    private void OnVoiceActivityChanged(bool isTalking)
    {
        if (!_isInVoiceChannel) return; // Ignore if not in voice
        if (!SettingsManager.Current.ShowVoiceActivity) return; // Ignore if disabled
        
        Dispatcher.Invoke(() =>
        {
            if (_muteState == MuteState.Unmuted || _muteState == MuteState.Talking)
            {
                _muteState = isTalking ? MuteState.Talking : MuteState.Unmuted;
                _tray.SetState(_muteState);
            }
        });
    }

    private void ExitApp()
    {
        _isExiting = true;
        _voiceCheckTimer?.Dispose();
        _audio.Dispose();
        _keys.Unregister();
        _tray.Dispose();
        _bigIcon?.Dispose();
        _smallIcon?.Dispose();
        Browser.Dispose();
        Cef.Shutdown();
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting && SettingsManager.Current.CloseToTray) 
        { 
            e.Cancel = true; 
            Hide(); 
        }
        base.OnClosing(e);
    }
}

public class DiscordRequestHandler : CefSharp.Handler.RequestHandler
{
    protected override bool OnCertificateError(
        IWebBrowser b, IBrowser browser, CefErrorCode errorCode,
        string requestUrl, ISslInfo sslInfo, IRequestCallback callback)
    {
        callback.Continue(true);
        return true;
    }

    protected override void OnRenderProcessTerminated(
        IWebBrowser browserControl, IBrowser browser, CefTerminationStatus status)
    {
        // Reload if renderer crashes
        if (status == CefTerminationStatus.AbnormalTermination || 
            status == CefTerminationStatus.ProcessCrashed ||
            status == CefTerminationStatus.ProcessWasKilled)
        {
            browser.Reload();
        }
    }
}

public class NoContextMenuHandler : CefSharp.Handler.ContextMenuHandler
{
    protected override void OnBeforeContextMenu(
        IWebBrowser b, IBrowser browser, IFrame frame,
        IContextMenuParams parameters, IMenuModel model) => model.Clear();
}

public class MediaPermissionHandler : CefSharp.Handler.PermissionHandler
{
    protected override bool OnRequestMediaAccessPermission(
        IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame,
        string requestingOrigin, MediaAccessPermissionType requestedPermissions,
        IMediaAccessCallback callback)
    {
        // Auto-grant ALL media permissions including screen capture
        // The screen picker will be handled by CEF's built-in UI
        using (callback)
        {
            callback.Continue(requestedPermissions);
        }
        return true;
    }

    protected override bool OnShowPermissionPrompt(
        IWebBrowser chromiumWebBrowser, IBrowser browser, ulong promptId,
        string requestingOrigin, CefSharp.PermissionRequestType requestedPermissions,
        IPermissionPromptCallback callback)
    {
        // Auto-accept all permission prompts
        using (callback)
        {
            callback.Continue(PermissionRequestResult.Accept);
        }
        return true;
    }
}

public class ScreenCaptureDisplayHandler : CefSharp.Handler.DisplayHandler
{
    // This handler ensures screen capture dialogs are shown properly
}

public class ScreenShareLifeSpanHandler : CefSharp.Handler.LifeSpanHandler
{
    protected override bool OnBeforePopup(
        IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame,
        string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition,
        bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo,
        IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser? newBrowser)
    {
        // Allow popups for screen picker (chrome://webrtc-internals or picker dialogs)
        if (targetUrl.Contains("webrtc") || targetUrl.Contains("picker") || targetUrl.Contains("chrome://"))
        {
            newBrowser = null;
            return false; // Let CEF handle it
        }
        
        newBrowser = null;
        return base.OnBeforePopup(chromiumWebBrowser, browser, frame, targetUrl, targetFrameName,
            targetDisposition, userGesture, popupFeatures, windowInfo, browserSettings,
            ref noJavascriptAccess, out newBrowser);
    }
}
