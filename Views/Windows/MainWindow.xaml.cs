using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Threading.Tasks;
using Cordex.Core;

namespace Cordex;

public partial class MainWindow : Window
{
    private readonly TrayManager    _tray  = new();
    private readonly KeybindManager _keys  = new();
    private readonly AudioMonitor   _audio = new();

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
        InitializeComponent();

        var area = SystemParameters.WorkArea;
        Width  = area.Width  * 0.85;
        Height = area.Height * 0.85;

        if (System.IO.File.Exists(IconPath))
            Icon = new System.Windows.Media.Imaging.BitmapImage(
                new Uri(IconPath, UriKind.Absolute));

        Loaded            += OnWindowLoaded;
        SourceInitialized += OnSourceInitialized;
        StateChanged      += OnStateChanged;

        _ = InitializeWebViewAsync();
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

    // ── WebView2 Init & Event Handlers ────────────────────────────────────────
    private async Task InitializeWebViewAsync()
    {
        try
        {
            SessionManager.EnsureDirectories();

            var options = new CoreWebView2EnvironmentOptions();
            var args = new List<string>
            {
                "--autoplay-policy=no-user-gesture-required",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-renderer-backgrounding",
                "--disable-features=WebRtcHideLocalIpsWithMdns,InterestFeedContentSuggestions,BlinkGenPropertyTrees,SafeBrowsing",
                "--enable-features=AudioWorkletThreadRealtimePriority,WebAssemblySimd,WebAssemblyThreads,MediaRouter",
                "--disable-plugins-discovery",
                "--disable-print-preview",
                "--disable-spell-checking",
                "--webrtc-max-start-bitrate-kbps=2500",

                // Aggressive memory optimization arguments
                "--disable-extensions",
                "--disable-component-update",
                "--disable-background-networking",
                "--disable-sync",
                "--disable-default-apps",
                "--disable-domain-reliability",
                "--disable-logging",
                "--disable-breakpad",
                "--memory-pressure-thresholds=1"
            };

            var s = SettingsManager.Current;

            if (!s.HardwareAcceleration)
            {
                args.Add("--disable-gpu");
                args.Add("--disable-gpu-rasterization");
                args.Add("--disable-accelerated-video-decode");
            }
            else
            {
                args.Add("--enable-gpu-rasterization");
                args.Add("--enable-accelerated-video-decode");
                args.Add("--enable-zero-copy");
            }

            if (s.EnablePerformanceLimits)
            {
                args.Add("--renderer-process-limit=1"); // Enforce single renderer process for maximum memory savings
                if (s.MaxRamMB >= 100)
                {
                    int jsHeapLimit = (s.MaxRamMB * 40) / 100;
                    args.Add($"--js-flags=--max-old-space-size={jsHeapLimit}");
                }
            }

            options.AdditionalBrowserArguments = string.Join(" ", args);

            // Set up WebView2 environment using our existing cache directory and options
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: SessionManager.CacheDirectory,
                options: options
            );

            await Browser.EnsureCoreWebView2Async(env);
            PerformanceManager.WebView2BrowserProcessId = (int)Browser.CoreWebView2.BrowserProcessId;

            // Configure browser settings
            Browser.CoreWebView2.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/126.0.0.0 Safari/537.36";

            Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            // Wire up event handlers
            Browser.CoreWebView2.PermissionRequested += OnPermissionRequested;
            Browser.CoreWebView2.NewWindowRequested  += OnNewWindowRequested;
            Browser.NavigationCompleted              += OnBrowserNavigationCompleted;
            Browser.WebMessageReceived              += OnWebMessageReceived;

            // Register web resource filter for blocking telemetry/tracking
            Browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            Browser.CoreWebView2.WebResourceRequested += OnWebResourceRequested;

            Browser.Source = new Uri("https://discord.com/app");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to initialize WebView2:\n{ex}",
                "Cordex Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        // Automatically allow mic and camera permissions
        if (e.PermissionKind is CoreWebView2PermissionKind.Microphone 
                             or CoreWebView2PermissionKind.Camera)
        {
            e.State = CoreWebView2PermissionState.Allow;
            e.Handled = true;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        string targetUrl = e.Uri;
        if (string.IsNullOrEmpty(targetUrl) || targetUrl.StartsWith("about:"))
            return;

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
            // Open internal links in a pop-up window using a new WebView2 control
            var popupWebview = new WebView2();
            var win = new Window
            {
                Title                 = "Cordex",
                Width                 = 1000,
                Height                = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content               = popupWebview
            };

            win.Loaded += async (s, ev) =>
            {
                try
                {
                    var env = await CoreWebView2Environment.CreateAsync(
                        browserExecutableFolder: null,
                        userDataFolder: SessionManager.CacheDirectory
                    );
                    await popupWebview.EnsureCoreWebView2Async(env);
                    popupWebview.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    popupWebview.Source = new Uri(targetUrl);
                }
                catch { }
            };

            win.Show();
            e.Handled = true;
        }
        else if (targetUrl.StartsWith("http://") || targetUrl.StartsWith("https://"))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = targetUrl,
                    UseShellExecute = true
                });
            }
            catch { }
            e.Handled = true;
        }
    }

    private static bool IsCdnUrl(string url) =>
        url.Contains("cdn.discordapp.com")          ||
        url.Contains("media.discordapp.net")        ||
        url.Contains("discordapp.com/attachments")  ||
        url.Contains("discord.com/attachments")     ||
        url.Contains("images-ext-1.discordapp.net") ||
        url.Contains("images-ext-2.discordapp.net");

    private static bool IsActivityUrl(string url) =>
        url.Contains("discord.com/activities")    ||
        url.Contains("discordapp.com/activities") ||
        url.Contains("youtube.com")               ||
        url.Contains("ytimg.com")                 ||
        url.Contains("googlevideo.com")           ||
        url.Contains("googleapis.com");

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var    s   = SettingsManager.Current;
        string url = e.Request.Uri.ToLowerInvariant();

        if (IsCdnUrl(url) || IsActivityUrl(url))
            return;

        bool cancel = false;

        if (s.BlockFingerprinting && (url.Contains("api.js") || url.Contains("cdn-cgi")))
            cancel = true;
        else if (s.BlockTelemetry && (url.Contains("/science") || url.Contains("/tracing")))
            cancel = true;
        else if (s.BlockSentry && url.Contains("sentry"))
            cancel = true;
        else if (s.BlockTypingIndicator && url.Contains("/typing"))
            cancel = true;
        else if (s.BlockAnimatedAssets && (url.EndsWith(".gif") || url.Contains("/a_")))
            cancel = true;
        else if (s.BlockCrashReports && (url.Contains("errors.discord.com") || url.Contains("/report-")))
            cancel = true;
        else if (s.BlockExperiments && url.Contains("/experiments"))
            cancel = true;
        else if (s.ReduceBackgroundActivity && (url.Contains("/science") ||
            url.Contains("/outbound-promotions") || url.Contains("/metrics")))
            cancel = true;
        else if (s.BlockMarketing && (url.Contains("/quests/") || url.Contains("/promotions") ||
            url.Contains("/referrals/") || url.Contains("/collectibles-marketing")))
            cancel = true;
        else if (s.BlockDetectableGames && (url.Contains("/games/detectable") ||
            url.Contains("/non-games/detectable")))
            cancel = true;
        else if (s.BlockExternalImages && (url.Contains("images-ext-1.discordapp.net") ||
            url.Contains("images-ext-2.discordapp.net")))
            cancel = true;
        else if (s.BlockStatusPolling && (url.Contains("status.discord.com") ||
            url.Contains("/scheduled-maintenance")))
            cancel = true;
        else if (s.BlockContentInventory && url.Contains("/content-inventory/"))
            cancel = true;
        else if (s.BlockVendorChunks && url.Contains("/assets/vendors~"))
            cancel = true;
        else if (s.BlockDiscordStore && url.Contains("/store/"))
            cancel = true;
        else if (s.BlockUserSurveys && (url.Contains("/survey") || url.Contains("/premium-survey")))
            cancel = true;
        else if (s.BlockStickerPacks && url.Contains("/sticker-packs"))
            cancel = true;
        else if (s.BlockSpotifyAndMetadata && (
            url.Contains("api.spotify.com") ||
            url.Contains("games.json") ||
            url.Contains("non-games.json") ||
            url.Contains("/billing/user-offer") ||
            url.Contains("storefront-config") ||
            url.Contains("applications/public") ||
            url.Contains("collectibles-marketing") ||
            url.Contains("exclusions") ||
            url.Contains("checkout-recovery") ||
            url.Contains("subscriptions?include_inactive") ||
            url.Contains("eligibility") ||
            url.Contains("decision?placement=") ||
            url.Contains("get-decisions?placement=") ||
            url.Contains("tokens?application_ids=") ||
            url.Contains("games?game_ids=")
        ))
            cancel = true;

        if (cancel)
        {
            e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(
                null, 403, "Forbidden", "Blocked by Cordex settings");
        }
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

    private void OnBrowserNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
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

            Dispatcher.Invoke(InjectVoiceStateObserver);
            Dispatcher.Invoke(InjectMicAgcPatch);
            Dispatcher.Invoke(InjectDragDropFix);
            Dispatcher.Invoke(InjectCustomTitleBar);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(message)) return;

        Dispatcher.Invoke(() =>
        {
            if (message.StartsWith("VoiceState:"))
            {
                var parts = message.Split(':');
                if (parts.Length >= 4)
                    UpdateVoiceState(parts[1] == "true", parts[2] == "true", parts[3] == "true");
            }
            else if (message == "window:close")
            {
                if (SettingsManager.Current.CloseToTray) Hide();
                else ExitApp();
            }
            else if (message == "window:maximize")
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else if (message == "window:minimize")
            {
                if (SettingsManager.Current.MinimizeToTray) Hide();
                else WindowState = WindowState.Minimized;
            }
            else if (message == "window:settings")
            {
                var win = new SettingsWindow { Owner = this };
                win.KeybindsSaved   += ReloadKeybinds;
                win.SettingsChanged += OnSettingsChanged;
                win.ShowDialog();
            }
            else if (message == "window:drag")
            {
                try { DragMove(); } catch { }
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
                if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage)
                    window.chrome.webview.postMessage('VoiceState:' + s.inVoice + ':' + s.isMuted + ':' + s.isDeafened);
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



    private void InjectDragDropFix()
    {
        const string script = @"
(function() {
    if (window.__cordexDragDropFixed) return;
    window.__cordexDragDropFixed = true;
    
    // Force enable drag and drop by ensuring draggable attribute is respected
    document.addEventListener('dragstart', function(e) {
        // Allow all drag operations
        if (e.dataTransfer) {
            e.dataTransfer.effectAllowed = 'all';
        }
    }, true);
    
    document.addEventListener('dragover', function(e) {
        // Prevent default to allow drop
        e.preventDefault();
        if (e.dataTransfer) {
            e.dataTransfer.dropEffect = 'move';
        }
    }, true);
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

    private void InjectCustomTitleBar()
    {
        string script = @"
(function() {
    if (document.getElementById('cordex-titlebar')) return;

    // Create style tag
    const style = document.createElement('style');
    style.id = 'cordex-titlebar-style';
    style.textContent = `
        #cordex-titlebar {
            position: fixed;
            top: 3px;
            left: 7px;
            z-index: 999999;
            display: flex;
            align-items: center;
            pointer-events: none;
            background: transparent;
            box-sizing: border-box;
        }
        .cordex-button-group {
            display: flex;
            align-items: center;
            pointer-events: auto;
            background: transparent;
            backdrop-filter: none;
            padding: 4px;
            border-radius: 5px;
            border: none;
            box-shadow: none;
            margin-top: 0;
        }
        .cordex-btn {
            width: 12px;
            height: 12px;
            border-radius: 50%;
            border: none;
            margin-right: 8px;
            cursor: pointer;
            padding: 0;
            transition: transform 0.1s ease, filter 0.1s ease;
        }
        .cordex-btn:hover {
            transform: scale(1.1);
            filter: brightness(1.2);
        }
        .cordex-close {
            background: #FF5F57;
        }
        .cordex-maximize {
            background: #28C840;
        }
        .cordex-minimize {
            background: #FEBC2E;
        }
        .cordex-separator {
            width: 1px;
            height: 14px;
            background: rgba(255, 255, 255, 0.15);
            margin: 0 10px 0 2px;
        }
        .cordex-settings {
            background: transparent;
            width: 18px;
            height: 18px;
            border-radius: 4px;
            display: flex;
            align-items: center;
            justify-content: center;
            color: #96989D;
            margin-right: 0;
        }
        .cordex-settings svg {
            width: 16px;
            height: 16px;
        }
        .cordex-settings:hover {
            color: #FFFFFF;
            background: rgba(255, 255, 255, 0.08);
        }
        /* Custom layout alignments to embed it directly inside Discord */
        /* Shift the server lists and sidebar header down to prevent overlaps */
        nav[class*='guilds-'] {
            padding-top: 36px !important;
        }
        [class*='sidebar-'] {
            margin-top: 36px !important;
            border-top-left-radius: 8px !important;
        }
        /* Make sure search bar and DM lists clear the overlay */
        [class*='searchBar-'] {
            padding-left: 80px !important;
            transition: padding-left 0.2s ease;
        }
        header[class*='header-'], [class*='container-'][class*='themed-'] {
            padding-left: 85px !important;
            transition: padding-left 0.2s ease;
        }
    `;
    document.head.appendChild(style);

    // Create titlebar container
    const titlebar = document.createElement('div');
    titlebar.id = 'cordex-titlebar';
    titlebar.innerHTML = `
        <div class='cordex-button-group'>
            <button class='cordex-btn cordex-close' title='Close to tray'></button>
            <button class='cordex-btn cordex-maximize' title='Maximize'></button>
            <button class='cordex-btn cordex-minimize' title='Minimize'></button>
            <div class='cordex-separator'></div>
            <button class='cordex-btn cordex-settings' title='Cordex Settings'>
                <svg viewBox='0 0 24 24'><path fill='currentColor' d='M19.43 12.98c.04-.32.07-.64.07-.98s-.03-.66-.07-.98l2.11-1.65c.19-.15.24-.42.12-.64l-2-3.46c-.12-.22-.39-.3-.61-.22l-2.49 1c-.52-.4-1.08-.73-1.69-.98l-.38-2.65C14.46 2.18 14.25 2 14 2h-4c-.25 0-.46.18-.49.42l-.38 2.65c-.61.25-1.17.59-1.69.98l-2.49-1c-.23-.09-.49 0-.61.22l-2 3.46c-.13.22-.07.49.12.64l2.11 1.65c-.04.32-.07.65-.07.98s.03.66.07.98l-2.11 1.65c-.19.15-.24.42-.12.64l2 3.46c.12.22.39.3.61.22l2.49-1c.52.4 1.08.73 1.69.98l.38 2.65c.03.24.24.42.49.42h4c.25 0 .46-.18.49-.42l.38-2.65c.61-.25 1.17-.59 1.69-.98l2.49 1c.23.09.49 0 .61-.22l2-3.46c.12-.22.07-.49-.12-.64l-2.11-1.65zM12 15.5c-1.93 0-3.5-1.57-3.5-3.5s1.57-3.5 3.5-3.5 3.5 1.57 3.5 3.5-1.57 3.5-3.5 3.5z'/></svg>
            </button>
        </div>
    `;
    document.body.appendChild(titlebar);

    // Wire events
    titlebar.querySelector('.cordex-close').addEventListener('click', () => {
        window.chrome.webview.postMessage('window:close');
    });
    titlebar.querySelector('.cordex-maximize').addEventListener('click', () => {
        window.chrome.webview.postMessage('window:maximize');
    });
    titlebar.querySelector('.cordex-minimize').addEventListener('click', () => {
        window.chrome.webview.postMessage('window:minimize');
    });
    titlebar.querySelector('.cordex-settings').addEventListener('click', () => {
        window.chrome.webview.postMessage('window:settings');
    });

    // Drag helper mapping
    document.addEventListener('mousedown', (e) => {
        const header = e.target.closest('[class*=\'title-\'], [class*=\'header-\'], #cordex-titlebar');
        if (header) {
            const interactive = e.target.closest('button, input, a, [role=\'button\'], [class*=\'clickable-\'], [class*=\'iconWrapper-\']');
            if (!interactive) {
                window.chrome.webview.postMessage('window:drag');
            }
        }
    });
})();
";
        ExecuteScript(script);
    }

    private void ExecuteScript(string script)
    {
        if (Browser.CoreWebView2 != null)
        {
            Dispatcher.Invoke(async () =>
            {
                try { await Browser.CoreWebView2.ExecuteScriptAsync(script); } catch { }
            });
        }
    }

    // ── DevTools (F9) ────────────────────────────────────────────────────────
    private void OnWindowPreviewKeyDown(object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F9)
        { Browser.CoreWebView2?.OpenDevToolsWindow(); e.Handled = true; }
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
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting && SettingsManager.Current.CloseToTray)
        { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
