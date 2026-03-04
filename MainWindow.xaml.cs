using System;
using System.ComponentModel;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;
using Nextcord.Core;
using Wpf.Ui.Controls;

namespace Nextcord;

public partial class MainWindow : FluentWindow
{
    private readonly TrayManager    _tray = new();
    private readonly KeybindManager _keys = new();

    private static bool _cefInitialized;
    private MuteState   _muteState = MuteState.Default;
    private bool        _isExiting = false;

    public MainWindow()
    {
        InitializeCef();
        InitializeComponent();

        Browser.RequestHandler  = new DiscordRequestHandler();
        Browser.MenuHandler     = new NoContextMenuHandler();
        Browser.BrowserSettings = new BrowserSettings
        {
            WindowlessFrameRate = 60
        };
        Browser.Address = "https://discord.com/app";

        Loaded += OnWindowLoaded;
    }

    private static void InitializeCef()
    {
        if (_cefInitialized) return;

        SessionManager.EnsureDirectories();

        var settings = new CefSettings
        {
            CachePath    = SessionManager.CacheDirectory,
            LogSeverity  = LogSeverity.Disable,
            UserAgent    = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/110.0.0.0 Safari/537.36"
        };

        // Mic / Camera / WebRTC
        settings.CefCommandLineArgs.Add("enable-media-stream",               "1");
        settings.CefCommandLineArgs.Add("use-fake-ui-for-media-stream",      "0");
        settings.CefCommandLineArgs.Add("enable-usermedia-screen-capturing", "1");

        // Performance
        settings.CefCommandLineArgs.Add("enable-gpu",                                 "1");
        settings.CefCommandLineArgs.Add("enable-gpu-rasterization",                   "1");
        settings.CefCommandLineArgs.Add("enable-zero-copy",                           "1");
        settings.CefCommandLineArgs.Add("disable-renderer-backgrounding",             "1");
        settings.CefCommandLineArgs.Add("disable-background-timer-throttling",        "1");
        settings.CefCommandLineArgs.Add("disable-backgrounding-occluded-windows",     "1");
        settings.CefCommandLineArgs.Add("autoplay-policy",                            "no-user-gesture-required");

        Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
        _cefInitialized = true;
    }

    // ── Startup ──────────────────────────────────────────────────────────────

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        InitTray();
        InitKeybinds();
    }

    private void InitTray()
    {
        _tray.Initialize();
        _tray.OpenRequested += ShowApp;
        _tray.MuteToggled   += ToggleMute;
        _tray.ExitRequested += ExitApp;
    }

    private void InitKeybinds()
    {
        _keys.Register(this);
        _keys.ToggleMute   += ToggleMute;
        _keys.ToggleDeafen += ToggleDeafen;
        _keys.FocusWindow  += ShowApp;
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private void ExecuteScript(string script)
    {
        try { Browser.GetMainFrame()?.ExecuteJavaScriptAsync(script); }
        catch { }
    }

    private void ToggleMute()
    {
        ExecuteScript(@"
            (function() {
                var btn = document.querySelector('[aria-label=""Mute""], [aria-label=""Unmute""]');
                if (btn) btn.click();
            })();
        ");
        _muteState = _muteState == MuteState.Muted ? MuteState.Unmuted : MuteState.Muted;
        _tray.SetState(_muteState);
    }

    private void ToggleDeafen()
    {
        ExecuteScript(@"
            (function() {
                var btn = document.querySelector('[aria-label=""Deafen""], [aria-label=""Undeafen""]');
                if (btn) btn.click();
            })();
        ");
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

    private void ExitApp()
    {
        _isExiting = true;
        _keys.Unregister();
        _tray.Dispose();
        Browser.Dispose();
        Cef.Shutdown();
        System.Windows.Application.Current.Shutdown();
    }

    // ── Window Behavior ──────────────────────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}

// ── Always allow mic/camera for Discord ──────────────────────────────────────

public class DiscordRequestHandler : CefSharp.Handler.RequestHandler
{
    protected override bool OnCertificateError(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        CefErrorCode errorCode,
        string requestUrl,
        ISslInfo sslInfo,
        IRequestCallback callback)
    {
        callback.Continue(true);
        return true;
    }
}

public class NoContextMenuHandler : CefSharp.Handler.ContextMenuHandler
{
    protected override void OnBeforeContextMenu(
        IWebBrowser browserControl,
        IBrowser browser,
        IFrame frame,
        IContextMenuParams parameters,
        IMenuModel model)
    {
        model.Clear();
    }
}
