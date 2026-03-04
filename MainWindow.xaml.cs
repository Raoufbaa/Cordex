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

        // Force taskbar presence by manipulating window visibility
        ShowWindow(hwnd, SW_HIDE);
        
        // Set extended window style to force taskbar button
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_APPWINDOW;   // Force taskbar button
        exStyle &= ~WS_EX_TOOLWINDOW; // Remove tool window flag
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        
        // Force window frame to update
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        
        ShowWindow(hwnd, SW_SHOW);

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
            UserAgent   = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36",
            WindowlessRenderingEnabled = false
        };

        settings.CefCommandLineArgs.Add("enable-media-stream", "1");
        settings.CefCommandLineArgs.Add("enable-usermedia-screen-capturing", "1");
        settings.CefCommandLineArgs.Add("enable-gpu", "1");
        settings.CefCommandLineArgs.Add("enable-gpu-rasterization", "1");
        settings.CefCommandLineArgs.Add("disable-renderer-backgrounding", "1");
        settings.CefCommandLineArgs.Add("disable-background-timer-throttling", "1");
        settings.CefCommandLineArgs.Add("disable-backgrounding-occluded-windows", "1");
        settings.CefCommandLineArgs.Add("autoplay-policy", "no-user-gesture-required");

        Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null);
        _cefInitialized = true;
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
        if (!e.IsLoading && !_discordLoaded)
        {
            _discordLoaded = true;
            Dispatcher.Invoke(() =>
            {
                // Wait a bit more for Discord to fully initialize
                System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(InitKeybinds);
                });
            });
        }
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
                        // In voice - check mute state
                        CheckMuteState();
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
            
            if (_muteState == MuteState.Unmuted)
                _audio.Start();
            else
                _audio.Stop();
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

    // ── Title Bar Buttons ─────────────────────────────────────────────────────
    private void BtnClose_Click(object sender, RoutedEventArgs e)    => Hide();
    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        win.KeybindsSaved += ReloadKeybinds;
        win.ShowDialog();
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
        
        // Start/stop audio monitoring based on mute state
        if (_muteState == MuteState.Unmuted)
            _audio.Start();
        else
            _audio.Stop();
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
        if (!_isExiting) { e.Cancel = true; Hide(); }
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
}

public class NoContextMenuHandler : CefSharp.Handler.ContextMenuHandler
{
    protected override void OnBeforeContextMenu(
        IWebBrowser b, IBrowser browser, IFrame frame,
        IContextMenuParams parameters, IMenuModel model) => model.Clear();
}
