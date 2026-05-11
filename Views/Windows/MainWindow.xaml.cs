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
    private readonly TrayManager    _tray  = new();
    private readonly KeybindManager _keys  = new();
    private readonly AudioMonitor   _audio = new();

    private static bool _cefInitialized;
    private MuteState   _muteState        = MuteState.Default;
    private bool        _isExiting        = false;
    private bool        _isInVoiceChannel = false;
    private bool        _discordLoaded    = false;

    private System.Drawing.Icon? _bigIcon;
    private System.Drawing.Icon? _smallIcon;

    private static readonly string IconPath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");

    private const int    WM_NCHITTEST      = 0x0084;
    private const int    WM_GETMINMAXINFO  = 0x0024;
    private const int    WM_SETICON        = 0x0080;
    private const int    ICON_SMALL        = 0;
    private const int    ICON_BIG          = 1;
    private const int    HTCAPTION         = 2;
    private const int    HTLEFT            = 10;
    private const int    HTRIGHT           = 11;
    private const int    HTTOP             = 12;
    private const int    HTTOPLEFT         = 13;
    private const int    HTTOPRIGHT        = 14;
    private const int    HTBOTTOM          = 15;
    private const int    HTBOTTOMLEFT      = 16;
    private const int    HTBOTTOMRIGHT     = 17;
    private const int    GWL_EXSTYLE       = -20;
    private const int    WS_EX_APPWINDOW   = 0x00040000;
    private const int    WS_EX_TOOLWINDOW  = 0x00000080;
    private const uint   MONITOR_NEAREST   = 0x00000002;
    private const uint   ABM_GETSTATE      = 0x00000004;
    private const int    ABS_AUTOHIDE      = 0x00000001;
    private const uint   SWP_NOMOVE        = 0x0002;
    private const uint   SWP_NOSIZE        = 0x0001;
    private const uint   SWP_NOZORDER      = 0x0004;
    private const uint   SWP_FRAMECHANGED  = 0x0020;
    private const int    SW_HIDE           = 0;
    private const int    SW_SHOW           = 5;
    private const uint   DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int    DWMWCP_ROUND      = 2;
    private const int    DWMWCP_DONOTROUND = 1;

    private const double ResizeBorder  = 8;
    private const double DragHeight    = 36;
    private const double LeftReserve   = 130;
    private const double RightReserve  = 80;

    private static readonly System.Windows.Media.SolidColorBrush BorderColor =
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter
            .ConvertFromString("#3A3C40"));

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

        if (System.IO.File.Exists(IconPath))
            Icon = new System.Windows.Media.Imaging.BitmapImage(
                new Uri(IconPath, UriKind.Absolute));

        Browser.RequestHandler    = new DiscordRequestHandler();
        Browser.MenuHandler       = new NoContextMenuHandler();
        Browser.PermissionHandler = new MediaPermissionHandler();
        Browser.LifeSpanHandler   = new DiscordLifeSpanHandler();
        Browser.DownloadHandler   = new DiscordDownloadHandler();
        Browser.BrowserSettings   = new BrowserSettings { WindowlessFrameRate = 60 };
        
        Browser.JavascriptObjectRepository.Settings.LegacyBindingEnabled = true;
        Browser.JavascriptObjectRepository.Register("cordexDesktop", new DesktopSourceProvider(), BindingOptions.DefaultBinder);

        Browser.Address           = "https://discord.com/app";
        Browser.JavascriptMessageReceived += OnJavascriptMessageReceived;
        Browser.LoadingStateChanged       += OnBrowserLoadingStateChanged;

        Loaded            += OnWindowLoaded;
        SourceInitialized += OnSourceInitialized;
        StateChanged      += OnStateChanged;
    }

    // ── Win32 Hook ───────────────────────────────────────────────────────────
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

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

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |=  WS_EX_APPWINDOW;
        exStyle &= ~WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

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

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
                           ref bool handled)
    {
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

        if (msg == WM_NCHITTEST)
        {
            int sx = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            int sy = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

            var    pt = PointFromScreen(new System.Windows.Point(sx, sy));
            double x  = pt.X, y = pt.Y, w = ActualWidth, h = ActualHeight;

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
            UserAgent   =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/120.0.0.0 Safari/537.36",
            WindowlessRenderingEnabled = false
        };

        bool hwAccel = SettingsManager.Current.HardwareAcceleration;

        settings.CefCommandLineArgs.Add("enable-media-stream",                "1");
        settings.CefCommandLineArgs.Add("enable-usermedia-screen-capturing",  "1");
        settings.CefCommandLineArgs.Add("enable-usermedia-screen-capture",    "1");
        settings.CefCommandLineArgs.Add("autoplay-policy",                    "no-user-gesture-required");
        settings.CefCommandLineArgs.Add("enforce-webrtc-ip-permission-check", "0");
        settings.CefCommandLineArgs.Add("no-sandbox",                         "1");

        if (hwAccel)
        {
            settings.CefCommandLineArgs.Add("enable-gpu",                      "1");
            settings.CefCommandLineArgs.Add("enable-gpu-rasterization",        "1");
            settings.CefCommandLineArgs.Add("enable-accelerated-video-decode", "1");
            settings.CefCommandLineArgs.Add("enable-zero-copy",                "1");
        }
        else
        {
            settings.CefCommandLineArgs.Add("use-gl",                           "swiftshader");
            settings.CefCommandLineArgs.Add("disable-accelerated-video-decode", "1");
        }

        settings.CefCommandLineArgs.Add("force-color-profile", "srgb");

        // ALWAYS disable timer throttling — WebRTC voice keepalives and DTLS
        // heartbeats MUST fire on schedule even when the window is minimized
        // or occluded, otherwise connections stall at DTLS_CONNECTING.
        settings.CefCommandLineArgs.Add("disable-background-timer-throttling",    "1");
        settings.CefCommandLineArgs.Add("disable-backgrounding-occluded-windows", "1");

        if (SettingsManager.Current.ReduceBackgroundActivity)
        {
            settings.CefCommandLineArgs.Add("enable-ipc-flooding-protection", "1");
        }
        else
        {
            settings.CefCommandLineArgs.Add("disable-renderer-backgrounding",  "1");
            settings.CefCommandLineArgs.Add("disable-ipc-flooding-protection", "1");
        }

        settings.CefCommandLineArgs.Add("enable-quic",               "1");
        settings.CefCommandLineArgs.Add("disable-pdf-extension",     "1");
        settings.CefCommandLineArgs.Add("disable-plugins-discovery", "1");
        settings.CefCommandLineArgs.Add("disable-spell-checking",    "1");
        settings.CefCommandLineArgs.Add("disable-print-preview",     "1");
        settings.CefCommandLineArgs.Add("disable-component-update",  "1");
        settings.CefCommandLineArgs.Add("disable-logging",           "1");
        settings.CefCommandLineArgs.Add("disable-metrics",           "1");
        settings.CefCommandLineArgs.Add("disable-metrics-reporter",  "1");
        
        // WebRTC network resilience for high-latency/packet-loss scenarios
        settings.CefCommandLineArgs.Add("webrtc-max-start-bitrate-kbps",         "2500");
        settings.CefCommandLineArgs.Add("webrtc-max-cpu-consumption-percentage", "100");
        settings.CefCommandLineArgs.Add("webrtc-stun-probe-trial",               "Enabled");
        settings.CefCommandLineArgs.Add("force-fieldtrials",                     "WebRTC-FlexFEC-03-Advertised/Enabled/");


        settings.CefCommandLineArgs.Add("disable-features",
            "WebRtcHideLocalIpsWithMdns," +
            "InterestFeedContentSuggestions," +
            "BlinkGenPropertyTrees," +
            "SafeBrowsing");

        settings.CefCommandLineArgs.Add("enable-features",
            "AudioWorkletThreadRealtimePriority," +
            "WebAssemblySimd," +
            "WebAssemblyThreads," +
            "MediaRouter");

        if (SettingsManager.Current.EnablePerformanceLimits)
        {
            if (SettingsManager.Current.MaxRamMB >= 100)
            {
                int  maxRamMB    = SettingsManager.Current.MaxRamMB;
                int  jsHeapLimit = (maxRamMB * 40) / 100;
                long diskCache   = Math.Min(100 * 1024 * 1024, maxRamMB * 1024L * 1024L / 2);
                long mediaCache  = Math.Min(50  * 1024 * 1024, maxRamMB * 1024L * 512L);

                settings.CefCommandLineArgs.Add("renderer-process-limit", "3");
                settings.CefCommandLineArgs.Add("max-old-space-size",     jsHeapLimit.ToString());
                settings.CefCommandLineArgs.Add("disk-cache-size",        diskCache.ToString());
                settings.CefCommandLineArgs.Add("media-cache-size",       mediaCache.ToString());
            }

            settings.CefCommandLineArgs.Add("max-gum-fps",  "60");
            settings.CefCommandLineArgs.Add("disable-sync", "1");
        }

        Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null);
        _cefInitialized = true;

        ApplyWebRtcRequestContextPreferences();
    }

    private static void ApplyWebRtcRequestContextPreferences()
    {
        _ = Cef.UIThreadTaskFactory.StartNew(() =>
        {
            var ctx = Cef.GetGlobalRequestContext();
            if (ctx == null) return;
            ctx.SetPreference("webrtc.ip_handling_policy",      "all_interfaces", out _);
            ctx.SetPreference("webrtc.multiple_routes_enabled", true,             out _);
            ctx.SetPreference("webrtc.nonproxied_udp_enabled",  true,             out _);
        });
    }

    // ── Startup ──────────────────────────────────────────────────────────────
    private void OnWindowLoaded(object sender, RoutedEventArgs e) => InitTray();

    private void InitTray()
    {
        _tray.Initialize();
        _tray.OpenRequested += ShowApp;
        _tray.MuteToggled   += ToggleMute;
        _tray.ExitRequested += ExitApp;
        // Audio monitoring removed for performance
    }

    private void InitKeybinds()
    {
        _keys.Register(this);
        _keys.ToggleMute   += ToggleMute;
        _keys.ToggleDeafen += ToggleDeafen;
        _keys.FocusWindow  += ShowApp;
    }

    private void OnBrowserLoadingStateChanged(object? sender,
        LoadingStateChangedEventArgs e)
    {
        if (!e.IsLoading)
        {
            if (!_discordLoaded)
            {
                _discordLoaded = true;
                Dispatcher.Invoke(() =>
                {
                    System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ =>
                        Dispatcher.Invoke(() =>
                        {
                            InitKeybinds();
                            if (SettingsManager.Current.ReducedMotion)
                                ApplyReducedMotion();
                        }));
                });
            }

            Dispatcher.Invoke(InjectScreenSharePicker);
            Dispatcher.Invoke(InjectWebRtcRecoveryScript);
            Dispatcher.Invoke(InjectVoiceStateObserver);
            Dispatcher.Invoke(InjectMicAgcPatch);
        }
    }

    private void OnJavascriptMessageReceived(object? sender,
        JavascriptMessageReceivedEventArgs e)
    {
        if (e.Message is not string message) return;
        Dispatcher.Invoke(() =>
        {
            if (message.StartsWith("VoiceState:"))
            {
                var parts = message.Split(':');
                if (parts.Length >= 4)
                    UpdateVoiceState(parts[1] == "true", parts[2] == "true", parts[3] == "true");
            }
        });
    }

    private void UpdateVoiceState(bool inVoice, bool isMuted, bool isDeafened)
    {
        if (inVoice != _isInVoiceChannel)
        {
            bool wasInVoice   = _isInVoiceChannel;
            _isInVoiceChannel = inVoice;

            // Notify performance manager about voice connection state
            // This prevents aggressive CPU/RAM throttling during WebRTC connections
            PerformanceManager.SetVoiceConnectionState(inVoice);

            // Only attempt to auto-mute if transitioning INTO voice chat
            if (_isInVoiceChannel && !wasInVoice && SettingsManager.Current.AutomaticallyMute && !isMuted)
            {
                System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
                    Dispatcher.Invoke(() =>
                    {
                        ExecuteScript(@"(function(){var b=document.querySelector('[aria-label=""Mute""]');if(b)b.click();})();");
                    }));
            }
        }

        // Update mute state
        var newState = isDeafened ? MuteState.Deafened : (isMuted ? MuteState.Muted : MuteState.Unmuted);
        _muteState = newState;
        _tray.SetState(_muteState);

        // Audio monitoring removed for performance
    }

    private void SyncAudio()
    {
        if (SettingsManager.Current.ShowVoiceActivity &&
            SettingsManager.Current.EnableAudioMonitoring &&
            _muteState == MuteState.Unmuted)
            _audio.Start();
        else
            _audio.Stop();
    }

    /// <summary>
    /// Patches navigator.mediaDevices.getUserMedia so that every audio
    /// capture request from Discord's WebRTC stack has
    ///   autoGainControl : false
    ///   noiseSuppression : false   (when DisableMicAgc is on)
    ///   echoCancellation : false   (when DisableMicAgc is on)
    /// injected into its audio constraints.  This eliminates Chromium's
    /// built-in software AGC that silently raises / lowers the mic level.
    ///
    /// The patch is a no-op guard so re-injecting on navigation is safe.
    /// When the setting is OFF the original getUserMedia is restored.
    /// </summary>
    private void InjectMicAgcPatch()
    {
        bool disable = SettingsManager.Current.DisableMicAgc;

        // language=javascript
        string script = $@"
(function() {{
    // ── Cordex Mic AGC Patch ─────────────────────────────────────────
    var DISABLE_AGC = {(disable ? "true" : "false")};

    // Restore native if feature is off and we previously patched
    if (!DISABLE_AGC) {{
        if (window.__cordexOrigGetUserMedia) {{
            navigator.mediaDevices.getUserMedia =
                window.__cordexOrigGetUserMedia;
            window.__cordexOrigGetUserMedia = null;
            window.__cordexMicAgcPatched    = false;
        }}
        return;
    }}

    // Already patched - nothing to do
    if (window.__cordexMicAgcPatched) return;

    var _origGUM = navigator.mediaDevices.getUserMedia.bind(
                        navigator.mediaDevices);
    window.__cordexOrigGetUserMedia = _origGUM;

    navigator.mediaDevices.getUserMedia = async function(constraints) {{
        try {{
            if (constraints && constraints.audio) {{
                var audioConstraints = (typeof constraints.audio === 'object')
                    ? Object.assign({{}}, constraints.audio)
                    : {{}};

                // Only patch microphone audio, NOT desktop audio (screen sharing)
                // Desktop audio uses chromeMediaSource: 'desktop' and should not be modified
                var isDesktopAudio = audioConstraints.mandatory && 
                                    audioConstraints.mandatory.chromeMediaSource === 'desktop';

                if (!isDesktopAudio) {{
                    // Force-disable all AGC / processing layers for microphone only
                    audioConstraints.autoGainControl  = false;
                    audioConstraints.noiseSuppression = false;
                    audioConstraints.echoCancellation = false;

                    constraints = Object.assign({{}}, constraints,
                        {{ audio: audioConstraints }});
                }}
            }}
        }} catch(e) {{
            // Silently ignore patch errors
        }}
        return _origGUM(constraints);
    }};

    window.__cordexMicAgcPatched = true;
}})();
";
        ExecuteScript(script);
    }

    private void InjectVoiceStateObserver()
    {
        const string script = @"
(function() {
    if (window.__cordexVoiceObserver) {
        if (typeof window.__cordexVoiceObserver.forceCheck === 'function')
            window.__cordexVoiceObserver.forceCheck();
        return;
    }

    let lastInVoice = null, lastMuted = null, lastDeaf = null, checkQueued = false;

    function readVoiceState() {
        const panels = document.querySelector('[class*=""panels_""]') || document.querySelector('[class*=""sidebar_""]');
        if (!panels) return null; // Discord UI hasn't rendered yet

        const inVoice = document.querySelector('[aria-label=""Disconnect""]') !== null;
        
        const muteBtn = document.querySelector('[aria-label=""Mute""]');
        const unmuteBtn = document.querySelector('[aria-label=""Unmute""]');
        const isMuted = unmuteBtn !== null || (muteBtn && muteBtn.getAttribute('aria-checked') === 'true');

        const deafBtn = document.querySelector('[aria-label=""Deafen""]');
        const undeafBtn = document.querySelector('[aria-label=""Undeafen""]');
        const isDeafened = undeafBtn !== null || (deafBtn && deafBtn.getAttribute('aria-checked') === 'true');

        return { inVoice, isMuted, isDeafened };
    }

    function postVoiceState() {
        try {
            const s = readVoiceState();
            if (!s) return; // Ignore if loading
            if (s.inVoice !== lastInVoice || s.isMuted !== lastMuted || s.isDeafened !== lastDeaf) {
                lastInVoice = s.inVoice; lastMuted = s.isMuted; lastDeaf = s.isDeafened;
                if (window.CefSharp && window.CefSharp.PostMessage)
                    window.CefSharp.PostMessage('VoiceState:' + s.inVoice + ':' + s.isMuted + ':' + s.isDeafened);
            }
        } catch(e) { /* Ignore errors */ }
    }

    function scheduleCheck() {
        if (checkQueued) return;
        checkQueued = true;
        setTimeout(() => { checkQueued = false; postVoiceState(); }, 100);
    }

    window.__cordexVoiceObserver = { forceCheck: scheduleCheck };

    document.addEventListener('click',            scheduleCheck, true);
    window.addEventListener('focus',              scheduleCheck, { passive: true });
    window.addEventListener('popstate',           scheduleCheck, { passive: true });
    window.addEventListener('hashchange',         scheduleCheck, { passive: true });
    document.addEventListener('visibilitychange', scheduleCheck, { passive: true });
    
    // Check frequently (1.5s) to quickly catch network-assigned states (e.g. server mutes)
    setInterval(() => { if (!document.hidden) postVoiceState(); }, 1500);

    postVoiceState();
})();
";
        ExecuteScript(script);
    }

    private void InjectScreenSharePicker()
    {
        const string script = @"
(function() {
    if (window.__cordexScreenShareFixed) return;
    window.__cordexScreenShareFixed = true;

    let _active = false, _stream = null;

    async function showPicker() {
        return new Promise(async (resolve, reject) => {
            await window.CefSharp.BindObjectAsync('cordexDesktop');
            const sourcesJson = await window.cordexDesktop.getSources();
            const sources = JSON.parse(sourcesJson);

            // Fetch Nitro status
            let hasNitro = false;
            try {
                let wp = window.webpackChunkdiscord_app.push([[Math.random()], {}, (r) => r]);
                window.webpackChunkdiscord_app.pop();
                for (const key in wp.c) {
                    const m = wp.c[key].exports;
                    if (m && m.default && m.default.getCurrentUser) {
                        const user = m.default.getCurrentUser();
                        hasNitro = user && user.premiumType > 0;
                        break;
                    }
                }
            } catch(e) { /* Ignore */ }

            const resOptions = hasNitro ? 
                [ {text:'1080p (Standard)', value:1080}, {text:'720p (Low bandwidth)', value:720}, {text:'1440p (High Res)', value:1440} ] :
                [ {text:'720p (Nitro Required for 1080p+)', value:720} ];

            const fpsOptions = hasNitro ? 
                [ {text:'60 FPS (Smoother)', value:60}, {text:'30 FPS (Classic)', value:30} ] :
                [ {text:'30 FPS (Nitro Required for 60fps)', value:30} ];

            const sourceOptions = sources.map(s => ({text: s.name, value: s.id}));

            // Create custom select helper
            function createCustomSelect(options, container) {
                const wrapper = document.createElement('div');
                wrapper.style.cssText = 'position:relative;width:100%;user-select:none;';
                
                const btn = document.createElement('div');
                btn.style.cssText = 'width:100%;padding:12px;background:rgba(0,0,0,0.4);border:1px solid rgba(255,255,255,0.05);border-radius:6px;font-size:14px;cursor:pointer;display:flex;justify-content:space-between;align-items:center;color:#fff;box-sizing:border-box;';
                
                const label = document.createElement('span');
                label.innerText = options[0].text;
                label.style.overflow = 'hidden';
                label.style.textOverflow = 'ellipsis';
                label.style.whiteSpace = 'nowrap';
                
                const arrow = document.createElement('span');
                arrow.innerHTML = '▼';
                arrow.style.fontSize = '10px';
                arrow.style.marginLeft = '8px';
                arrow.style.color = '#b5bac1';
                
                btn.appendChild(label);
                btn.appendChild(arrow);
                
                const list = document.createElement('div');
                list.className = 'cx-sel-list';
                list.style.cssText = 'position:absolute;top:100%;left:0;width:100%;background:#2b2d31;border:1px solid rgba(255,255,255,0.1);border-radius:6px;margin-top:4px;z-index:100000;max-height:160px;overflow-y:auto;display:none;box-shadow:0 8px 16px rgba(0,0,0,0.5);box-sizing:border-box;';
                
                // Add custom scrollbar styling globally
                if (!document.getElementById('cx-scrollbar-style')) {
                    const style = document.createElement('style');
                    style.id = 'cx-scrollbar-style';
                    style.innerHTML = '.cx-sel-list::-webkit-scrollbar { width: 6px; } .cx-sel-list::-webkit-scrollbar-thumb { background: #1e1f22; border-radius: 4px; }';
                    document.head.appendChild(style);
                }

                let value = options[0].value;
                
                options.forEach(opt => {
                    const item = document.createElement('div');
                    item.style.cssText = 'padding:10px 12px;color:#dbdee1;font-size:14px;cursor:pointer;transition:background 0.1s, color 0.1s;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
                    item.innerText = opt.text;
                    item.onmouseenter = () => { item.style.background = '#404249'; item.style.color = '#fff'; };
                    item.onmouseleave = () => { item.style.background = 'transparent'; item.style.color = '#dbdee1'; };
                    item.onclick = (e) => {
                        e.stopPropagation();
                        label.innerText = opt.text;
                        value = opt.value;
                        document.querySelectorAll('.cx-sel-list').forEach(l => l.style.display = 'none');
                    };
                    list.appendChild(item);
                });
                
                btn.onclick = (e) => {
                    e.stopPropagation();
                    const visible = list.style.display === 'block';
                    document.querySelectorAll('.cx-sel-list').forEach(l => l.style.display = 'none');
                    list.style.display = visible ? 'none' : 'block';
                };
                
                wrapper.appendChild(btn);
                wrapper.appendChild(list);
                container.appendChild(wrapper);
                
                return { getValue: () => value };
            }

            const overlay = document.createElement('div');
            overlay.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.6);backdrop-filter:blur(8px);z-index:999999;display:flex;align-items:center;justify-content:center;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif;opacity:0;transition:all 0.3s cubic-bezier(0.4, 0, 0.2, 1);';
            
            const dialog = document.createElement('div');
            dialog.style.cssText = 'background:rgba(30,31,34,0.65);backdrop-filter:blur(16px);border:1px solid rgba(255,255,255,0.08);border-radius:12px;padding:32px;min-width:440px;box-shadow:0 12px 32px rgba(0,0,0,0.5);transform:scale(0.95) translateY(10px);transition:all 0.3s cubic-bezier(0.4, 0, 0.2, 1);position:relative;overflow:visible;';
            
            const glow = document.createElement('div');
            glow.style.cssText = 'position:absolute;top:-50%;left:-50%;width:200%;height:200%;background:radial-gradient(circle at 50% 0%, rgba(88,101,242,0.15) 0%, transparent 50%);pointer-events:none;z-index:0;';
            dialog.appendChild(glow);

            const content = document.createElement('div');
            content.style.position = 'relative';
            content.style.zIndex = '1';
            
            content.innerHTML = `
                <h2 style='margin:0 0 8px 0;font-size:24px;font-weight:700;color:#f2f3f5;'>Stream Settings</h2>
                <p style='color:#b5bac1;font-size:14px;margin:0 0 24px 0;line-height:1.5;'>
                    Choose an application or screen to stream.
                </p>

                <div style='margin-bottom: 20px;'>
                    <label style='display:block;color:#b5bac1;font-size:12px;text-transform:uppercase;font-weight:700;margin-bottom:8px;'>Select Source</label>
                    <div id='cx-src-container'></div>
                </div>

                <div style='margin-bottom: 20px; display:flex; gap: 12px;'>
                    <div style='flex:1'>
                        <label style='display:block;color:#b5bac1;font-size:12px;text-transform:uppercase;font-weight:700;margin-bottom:8px;'>Stream Quality</label>
                        <div id='cx-res-container'></div>
                    </div>
                    <div style='flex:1'>
                        <label style='display:block;color:#b5bac1;font-size:12px;text-transform:uppercase;font-weight:700;margin-bottom:8px;'>Frame Rate</label>
                        <div id='cx-fps-container'></div>
                    </div>
                </div>

                <div style='margin-bottom: 28px; display:flex; align-items: center;'>
                    <input type='checkbox' id='cx-audio' checked style='width: 18px; height: 18px; margin-right: 10px; cursor: pointer; accent-color: #5865F2;'>
                    <label for='cx-audio' style='color:#fff;font-size:14px;font-weight:600;cursor:pointer;'>Share System/Game Audio</label>
                </div>

                <div style='display:flex;gap:12px;'>
                    <button id='cx-cancel' style='flex:1;padding:14px;background:transparent;color:#f2f3f5;border:1px solid rgba(255,255,255,0.1);border-radius:6px;font-size:14px;font-weight:600;cursor:pointer;transition:all 0.2s ease;'>Cancel</button>
                    <button id='cx-start' style='flex:2;padding:14px;background:#5865F2;color:#fff;border:none;border-radius:6px;font-size:14px;font-weight:600;cursor:pointer;transition:all 0.2s ease;box-shadow: 0 4px 12px rgba(88,101,242,0.4);'>Share Screen</button>
                </div>
            `;
            
            dialog.appendChild(content);
            overlay.appendChild(dialog);
            document.body.appendChild(overlay);

            // Initialize custom drop downs
            const srcCtrl = createCustomSelect(sourceOptions, document.getElementById('cx-src-container'));
            const resCtrl = createCustomSelect(resOptions, document.getElementById('cx-res-container'));
            const fpsCtrl = createCustomSelect(fpsOptions, document.getElementById('cx-fps-container'));

            // Close dropdowns if clicking outside
            overlay.addEventListener('click', (e) => {
                if (!e.target.closest('.cx-sel-list') && !e.target.closest('div[style*=""cursor:pointer""]')) {
                    document.querySelectorAll('.cx-sel-list').forEach(l => l.style.display = 'none');
                }
            });

            const cancelBtn = document.getElementById('cx-cancel');
            const startBtn = document.getElementById('cx-start');
            
            cancelBtn.onmouseenter = () => { cancelBtn.style.background = 'rgba(255,255,255,0.05)'; };
            cancelBtn.onmouseleave = () => { cancelBtn.style.background = 'transparent'; };
            startBtn.onmouseenter = () => { startBtn.style.filter = 'brightness(1.15)'; startBtn.style.transform = 'translateY(-1px)'; };
            startBtn.onmouseleave = () => { startBtn.style.filter = 'none'; startBtn.style.transform = 'none'; };
            startBtn.onmousedown = () => { startBtn.style.transform = 'translateY(1px)'; };
            startBtn.onmouseup = () => { startBtn.style.transform = 'translateY(-1px)'; };

            requestAnimationFrame(() => {
                overlay.style.opacity = '1';
                dialog.style.transform = 'scale(1) translateY(0)';
            });

            function closePicker() {
                overlay.style.opacity = '0';
                dialog.style.transform = 'scale(0.95) translateY(10px)';
                setTimeout(() => { if(document.body.contains(overlay)) document.body.removeChild(overlay); }, 300);
            }

            cancelBtn.onclick = () => { closePicker(); reject(new DOMException('User cancelled', 'NotAllowedError')); };
            startBtn.onclick = () => {
                const sourceId = srcCtrl.getValue();
                const res = parseInt(resCtrl.getValue(), 10);
                const fps = parseInt(fpsCtrl.getValue(), 10);
                const audio = document.getElementById('cx-audio').checked;
                closePicker();
                resolve({ sourceId, res, fps, audio });
            };
        });
    }

    navigator.mediaDevices.getDisplayMedia = async function(constraints) {
        if (_active && _stream) {
            const live = _stream.getTracks().some(t => t.readyState === 'live');
            _stream.getTracks().forEach(t => t.stop());
            _stream = null; _active = false; window.__cordexIsScreenSharing = false;
            if (live) await new Promise(r => setTimeout(r, 300));
        }
        try {
            const config = await showPicker();
            let w = 1920, h = 1080;
            if (config.res === 720) { w = 1280; h = 720; }
            if (config.res === 1440) { w = 2560; h = 1440; }

            // Route our custom source via getUserMedia directly using desktop capture flags!
            const stream = await navigator.mediaDevices.getUserMedia({
                video: {
                    mandatory: {
                        chromeMediaSource: 'desktop',
                        chromeMediaSourceId: config.sourceId,
                        minWidth: w,
                        maxWidth: w * 2,
                        minHeight: h,
                        maxHeight: h * 2,
                        minFrameRate: config.fps,
                        maxFrameRate: Math.max(60, config.fps)
                    }
                },
                audio: config.audio ? {
                    mandatory: {
                        chromeMediaSource: 'desktop'
                    }
                } : false
            });
            
            _stream = stream; _active = true; window.__cordexIsScreenSharing = true;
            stream.getTracks().forEach(t => {
                t.addEventListener('ended', () => { _active = false; _stream = null; window.__cordexIsScreenSharing = false; });
                t.addEventListener('mute',  () => { navigator.mediaDevices?.dispatchEvent(new Event('devicechange')); });
            });
            return stream;
        } catch(err) {
            _active = false; _stream = null;
            throw err;
        }
    };
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

    var _pending = false, _retries = 0, MAX_RETRIES = 3;
    var ICE_TIMEOUT  = 8000;   // 8 s — ICE checking phase (increased from 6s)
    var DTLS_TIMEOUT = 15000;  // 15 s — DTLS handshake after ICE resolves (increased from 10s)
    var DISC_TIMEOUT = 5000;   // 5 s — disconnected-state grace period

    /* ═══════════════════════════════════════════════════════════
       Layer 0 — Pre-generate DTLS certificate
       Eliminates an on-the-fly ECDSA key-gen that can stall
       the DTLS handshake when the CPU is under load.
       ═══════════════════════════════════════════════════════════ */
    var _pregenCert = null;
    var _certReady = false;
    try {
        var _OrigPC = window.RTCPeerConnection;
        if (_OrigPC && _OrigPC.generateCertificate) {
            _OrigPC.generateCertificate({ name: 'ECDSA', namedCurve: 'P-256' })
                .then(function(c) { 
                    _pregenCert = c; 
                    _certReady = true;
                })
                .catch(function(e) { /* Ignore */ });
        }
    } catch(e) {
        /* Certificate pre-generation not supported */
    }

    /* ═══════════════════════════════════════════════════════════
       Reconnect / recovery logic
       ═══════════════════════════════════════════════════════════ */
    function reconnect(reason) {
        if (_pending || window.__cordexIsScreenSharing) return;
        _pending = true; _retries++;
        
        if (_retries > MAX_RETRIES) {
            setTimeout(function() { location.reload(); }, 300);
            return;
        }
        
        var path = location.pathname;
        
        // First, try ICE restart if we have access to the connection
        var didIceRestart = false;
        try {
            // Try to find Discord's RTCPeerConnection instance and force ICE restart
            if (window.__cordexLastPC) {
                window.__cordexLastPC.restartIce();
                didIceRestart = true;
                
                // Give ICE restart 3 seconds to work before full reconnect
                setTimeout(function() {
                    if (_pending) {
                        doFullReconnect(path);
                    }
                }, 3000);
                return;
            }
        } catch(e) {
            /* ICE restart not available */
        }
        
        // If ICE restart not available, do full reconnect immediately
        if (!didIceRestart) {
            doFullReconnect(path);
        }
    }
    
    function doFullReconnect(path) {
        setTimeout(function() {
            var btn = document.querySelector('[aria-label=""Reconnect""]') ||
                      document.querySelector('[aria-label=""Try Again""]');
            if (btn) { 
                btn.click(); 
                setTimeout(function(){ _pending=false; }, 4000); 
                return; 
            }
            
            var dc = document.querySelector('[aria-label=""Disconnect""]');
            if (dc) {
                dc.click();
                setTimeout(function() {
                    try { history.pushState({},'',path); dispatchEvent(new PopStateEvent('popstate',{state:{}})); } catch(e){}
                    setTimeout(function() {
                        var jn = document.querySelector('[aria-label=""Join Voice Channel""]') ||
                                 document.querySelector('[class*=""joinButton""]');
                        if (jn) {
                            jn.click();
                        }
                        setTimeout(function(){ _pending=false; }, 4000);
                    }, 1200);
                }, 1000);
                return;
            }
            
            _pending = false;
            setTimeout(function() { location.reload(); }, 300);
        }, 400);
    }

    /* ═══════════════════════════════════════════════════════════
       Layer 1 — RTCPeerConnection patch
       Monitors native ICE + DTLS states with separate timers.
       ═══════════════════════════════════════════════════════════ */
    var _Orig = window.RTCPeerConnection;
    if (!_Orig) return;

    window.RTCPeerConnection = function(cfg, con) {
        /* Inject pre-generated DTLS cert to speed up handshake */
        if (_certReady && _pregenCert) {
            cfg = Object.assign({}, cfg || {});
            if (!cfg.certificates || cfg.certificates.length === 0) {
                cfg.certificates = [_pregenCert];
            }
        } else if (!_certReady) {
            /* DTLS cert not ready yet */
        }

        var pc = new _Orig(cfg, con);
        var _iceT = null, _dtlsT = null, _discT = null;
        var _iceOk = false;
        
        // Store reference for ICE restart capability
        window.__cordexLastPC = pc;

        function clr() {
            if (_iceT)  { clearTimeout(_iceT);  _iceT  = null; }
            if (_dtlsT) { clearTimeout(_dtlsT); _dtlsT = null; }
            if (_discT) { clearTimeout(_discT); _discT = null; }
        }

        function startIce(label) {
            if (_iceT) clearTimeout(_iceT);
            _iceOk = false;
            _iceT = setTimeout(function() {
                _iceT = null;
                var s = pc.iceConnectionState;
                if (s==='checking' || s==='new')
                    reconnect('ICE stuck '+ICE_TIMEOUT+'ms ('+label+', ice='+s+')');
            }, ICE_TIMEOUT);
        }

        function startDtls(label) {
            if (_dtlsT) clearTimeout(_dtlsT);
            _dtlsT = setTimeout(function() {
                _dtlsT = null;
                if (pc.connectionState==='connecting')
                    reconnect('DTLS stuck '+DTLS_TIMEOUT+'ms ('+label+')');
            }, DTLS_TIMEOUT);
        }

        function startDisc(label) {
            if (_discT) clearTimeout(_discT);
            _discT = setTimeout(function() {
                _discT = null;
                var s = pc.iceConnectionState;
                var c = pc.connectionState;
                if (s==='disconnected' || c==='disconnected')
                    reconnect('Disconnected '+DISC_TIMEOUT+'ms ('+label+')');
            }, DISC_TIMEOUT);
        }

        pc.addEventListener('iceconnectionstatechange', function() {
            var s = pc.iceConnectionState;
            if (s==='checking') {
                startIce('ice');
            } else if (s==='connected' || s==='completed') {
                _iceOk = true;
                if (_iceT) { clearTimeout(_iceT); _iceT = null; }
                if (_discT){ clearTimeout(_discT);_discT= null; }
                if (pc.connectionState==='connecting') startDtls('post-ice');
            } else if (s==='failed') {
                clr(); reconnect('ice=failed');
            } else if (s==='disconnected') {
                if (_iceT) { clearTimeout(_iceT); _iceT = null; }
                startDisc('ice-disc');
            } else if (s==='closed') {
                clr();
            }
        });

        pc.addEventListener('connectionstatechange', function() {
            var s = pc.connectionState;
            if (s==='connected') {
                clr(); _retries = 0; _iceOk = true;
            } else if (s==='connecting' && _iceOk) {
                startDtls('conn-renego');
            } else if (s==='failed') {
                clr(); reconnect('conn=failed');
            } else if (s==='disconnected') {
                startDisc('conn-disc');
            } else if (s==='closed') {
                clr();
            }
        });

        return pc;
    };
    window.RTCPeerConnection.prototype = _Orig.prototype;
    Object.getOwnPropertyNames(_Orig).forEach(function(k) {
        if (k==='prototype'||k==='length'||k==='name') return;
        try { window.RTCPeerConnection[k] = _Orig[k]; } catch(e){}
    });
})();
";
        ExecuteScript(script);
    }

    private void ApplyReducedMotion()
    {
        ExecuteScript(@"
(function(){
    var s = document.createElement('style');
    s.textContent = '*:not(iframe):not(video):not(canvas):not([class*=""message""]){animation-duration:0.01ms!important;animation-iteration-count:1!important;transition-duration:0.01ms!important;scroll-behavior:auto!important;}';
    document.head.appendChild(s);
})();");
    }

    private void ExecuteScript(string script)
        => Browser.GetMainFrame()?.ExecuteJavaScriptAsync(script);

    // ── DevTools (F9) ────────────────────────────────────────────────────────
    private void OnWindowPreviewKeyDown(object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F9)
        { Browser.ShowDevTools(); e.Handled = true; }
    }

    // ── Title Bar Buttons ────────────────────────────────────────────────────
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    { if (SettingsManager.Current.CloseToTray) Hide(); else ExitApp(); }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    { if (SettingsManager.Current.MinimizeToTray) Hide(); else WindowState = WindowState.Minimized; }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        win.KeybindsSaved   += ReloadKeybinds;
        win.SettingsChanged += OnSettingsChanged;
        win.ShowDialog();
    }

    private void OnSettingsChanged()
    {
        _audio.RefreshSettings();
        if (_isInVoiceChannel) SyncAudio();
    }

    // ── Actions ──────────────────────────────────────────────────────────────
    private void ToggleMute()
    {
        ExecuteScript(
            @"(function(){var b=document.querySelector('[aria-label=""Mute""],[aria-label=""Unmute""]');if(b)b.click();})();");
        // We do NOT optimistic-toggle _muteState here. 
        // We let the JS VoiceState observer push the precise real state over.
    }

    private void ToggleDeafen()
    {
        ExecuteScript(
            @"(function(){var b=document.querySelector('[aria-label=""Deafen""],[aria-label=""Undeafen""]');if(b)b.click();})();");
    }

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

    private void ExitApp()
    {
        _isExiting = true;
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
        { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}

// ── DiscordRequestHandler ────────────────────────────────────────────────────
public class DiscordRequestHandler : CefSharp.Handler.RequestHandler
{
    protected override bool OnCertificateError(
        IWebBrowser b, IBrowser browser, CefErrorCode errorCode,
        string requestUrl, ISslInfo sslInfo, IRequestCallback callback)
    { callback.Continue(true); return true; }

    protected override void OnRenderProcessTerminated(
        IWebBrowser browserControl, IBrowser browser, CefTerminationStatus status)
    {
        if (status is CefTerminationStatus.AbnormalTermination
                    or CefTerminationStatus.ProcessCrashed
                    or CefTerminationStatus.ProcessWasKilled)
            browser.Reload();
    }

    protected override IResourceRequestHandler GetResourceRequestHandler(
        IWebBrowser cwb, IBrowser browser, IFrame frame, IRequest request,
        bool isNavigation, bool isDownload, string requestInitiator,
        ref bool disableDefaultHandling)
        => new DiscordResourceRequestHandler();
}

// ── DiscordResourceRequestHandler ───────────────────────────────────────────
public class DiscordResourceRequestHandler : CefSharp.Handler.ResourceRequestHandler
{
    // These CDN hosts must NEVER be blocked regardless of any user setting.
    // Discord's image lightbox (onZoom) renders <img> tags that fetch directly
    // from these hosts — blocking them produces the black overlay with no image.
    private static bool IsCdnUrl(string url) =>
        url.Contains("cdn.discordapp.com")          ||
        url.Contains("media.discordapp.net")        ||
        url.Contains("discordapp.com/attachments")  ||
        url.Contains("discord.com/attachments")     ||
        url.Contains("images-ext-1.discordapp.net") ||
        url.Contains("images-ext-2.discordapp.net");

    // Activity/game hosts must never be blocked for Watch Together to work.
    private static bool IsActivityUrl(string url) =>
        url.Contains("discord.com/activities")    ||
        url.Contains("discordapp.com/activities") ||
        url.Contains("youtube.com")               ||
        url.Contains("ytimg.com")                 ||
        url.Contains("googlevideo.com")           ||
        url.Contains("googleapis.com");

    protected override CefReturnValue OnBeforeResourceLoad(
        IWebBrowser cwb, IBrowser browser, IFrame frame,
        IRequest request, IRequestCallback callback)
    {
        var    s   = SettingsManager.Current;
        string url = request.Url.ToLowerInvariant();

        // ── WHITELIST: these hosts are never blocked by any setting ───────────
        if (IsCdnUrl(url) || IsActivityUrl(url))
            return CefReturnValue.Continue;

        // ── Sub-frames are never blocked (YouTube activity iframe) ────────────
        if (!frame.IsMain)
            return CefReturnValue.Continue;

        // ── Block rules (main frame only below this point) ────────────────────
        if (s.BlockFingerprinting && (url.Contains("api.js") || url.Contains("cdn-cgi")))
            return CefReturnValue.Cancel;
        if (s.BlockTelemetry && (url.Contains("/science") || url.Contains("/tracing")))
            return CefReturnValue.Cancel;
        if (s.BlockSentry && url.Contains("sentry"))
            return CefReturnValue.Cancel;
        if (s.BlockTypingIndicator && url.Contains("/typing"))
            return CefReturnValue.Cancel;
        if (s.BlockAnimatedAssets && (url.EndsWith(".gif") || url.Contains("/a_")))
            return CefReturnValue.Cancel;
        if (s.BlockCrashReports && (url.Contains("errors.discord.com") || url.Contains("/report-")))
            return CefReturnValue.Cancel;
        if (s.BlockExperiments && url.Contains("/experiments"))
            return CefReturnValue.Cancel;
        if (s.ReduceBackgroundActivity && (url.Contains("/science") ||
            url.Contains("/outbound-promotions") || url.Contains("/metrics")))
            return CefReturnValue.Cancel;
        if (s.BlockMarketing && (url.Contains("/quests/") || url.Contains("/promotions") ||
            url.Contains("/referrals/") || url.Contains("/collectibles-marketing")))
            return CefReturnValue.Cancel;
        if (s.BlockDetectableGames && (url.Contains("/games/detectable") ||
            url.Contains("/non-games/detectable")))
            return CefReturnValue.Cancel;
        if (s.BlockExternalImages && (url.Contains("images-ext-1.discordapp.net") ||
            url.Contains("images-ext-2.discordapp.net")))
            return CefReturnValue.Cancel;
        if (s.BlockStatusPolling && (url.Contains("status.discord.com") ||
            url.Contains("/scheduled-maintenance")))
            return CefReturnValue.Cancel;
        if (s.BlockContentInventory && url.Contains("/content-inventory/"))
            return CefReturnValue.Cancel;
        if (s.BlockVendorChunks && url.Contains("/assets/vendors~"))
            return CefReturnValue.Cancel;
        if (s.BlockDiscordStore && url.Contains("/store/"))
            return CefReturnValue.Cancel;
        if (s.BlockUserSurveys && (url.Contains("/survey") || url.Contains("/premium-survey")))
            return CefReturnValue.Cancel;
        if (s.BlockStickerPacks && url.Contains("/sticker-packs"))
            return CefReturnValue.Cancel;

        return CefReturnValue.Continue;
    }
}

// ── NoContextMenuHandler ─────────────────────────────────────────────────────
public class NoContextMenuHandler : CefSharp.Handler.ContextMenuHandler
{
    protected override void OnBeforeContextMenu(
        IWebBrowser b, IBrowser browser, IFrame frame,
        IContextMenuParams p, IMenuModel model) => model.Clear();
}

// ── MediaPermissionHandler ───────────────────────────────────────────────────
public class MediaPermissionHandler : CefSharp.Handler.PermissionHandler
{
    protected override bool OnRequestMediaAccessPermission(
        IWebBrowser cwb, IBrowser browser, IFrame frame,
        string requestingOrigin, MediaAccessPermissionType requestedPermissions,
        IMediaAccessCallback callback)
    { using (callback) { callback.Continue(requestedPermissions); } return true; }

    protected override bool OnShowPermissionPrompt(
        IWebBrowser cwb, IBrowser browser, ulong promptId,
        string requestingOrigin, CefSharp.PermissionRequestType requestedPermissions,
        IPermissionPromptCallback callback)
    { using (callback) { callback.Continue(PermissionRequestResult.Accept); } return true; }
}

// ── DiscordLifeSpanHandler ───────────────────────────────────────────────────
public class DiscordLifeSpanHandler : CefSharp.Handler.LifeSpanHandler
{
    protected override bool OnBeforePopup(
        IWebBrowser cwb, IBrowser browser, IFrame frame,
        string targetUrl, string targetFrameName,
        WindowOpenDisposition targetDisposition, bool userGesture,
        IPopupFeatures popupFeatures, IWindowInfo windowInfo,
        IBrowserSettings browserSettings, ref bool noJavascriptAccess,
        out IWebBrowser? newBrowser)
    {
        newBrowser = null;

        if (string.IsNullOrEmpty(targetUrl)     ||
            targetUrl.StartsWith("chrome://")   ||
            targetUrl.StartsWith("devtools://") ||
            targetUrl.StartsWith("about:"))
            return false;

        string lower = targetUrl.ToLowerInvariant();

        bool isInternal =
            targetUrl.StartsWith("blob:")              ||
            lower.Contains("cdn.discordapp.com")       ||
            lower.Contains("media.discordapp.net")     ||
            lower.Contains("discordapp.com/attachments")||
            lower.Contains("discord.com/attachments")  ||
            lower.Contains("discord.com/channels")     ||
            lower.Contains("discord.com/invite")       ||
            lower.Contains("discordapp.com")           ||
            lower.Contains("discord.com");

        if (isInternal)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var popupBrowser = new ChromiumWebBrowser(targetUrl)
                {
                    MenuHandler       = new NoContextMenuHandler(),
                    PermissionHandler = new MediaPermissionHandler(),
                    DownloadHandler   = new DiscordDownloadHandler()
                };
                var win = new System.Windows.Window
                {
                    Title                 = "Cordex",
                    Width                 = 1000,
                    Height                = 700,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                    Content               = popupBrowser
                };
                win.Show();
            });
            return true;
        }

        if (targetUrl.StartsWith("http://") || targetUrl.StartsWith("https://"))
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = targetUrl,
                        UseShellExecute = true
                    });
            }
            catch { }
        }

        return true;
    }
}

// ── DiscordDownloadHandler ───────────────────────────────────────────────────
public class DiscordDownloadHandler : CefSharp.Handler.DownloadHandler
{
    protected override void OnBeforeDownload(
        IWebBrowser cwb, IBrowser browser,
        DownloadItem downloadItem, IBeforeDownloadCallback callback)
    {
        if (callback.IsDisposed) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var name = downloadItem.SuggestedFileName ?? "download";
            var ext  = System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant();

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = name,
                Title    = "Save File",
                Filter   = string.IsNullOrEmpty(ext)
                    ? "All files (*.*)|*.*"
                    : $"{ext.ToUpperInvariant()} files (*.{ext})|*.{ext}|All files (*.*)|*.*"
            };

            using (callback)
            {
                if (dlg.ShowDialog() == true)
                    callback.Continue(dlg.FileName, showDialog: false);
            }
        });
    }

    protected override void OnDownloadUpdated(
        IWebBrowser cwb, IBrowser browser,
        DownloadItem downloadItem, IDownloadItemCallback callback) { }
}