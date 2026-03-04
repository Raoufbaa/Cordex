using System;
using System.IO;
using WinForms = System.Windows.Forms;

namespace Cordex.Core;

public enum MuteState { Default, Muted, Unmuted, Talking }

public class TrayManager : IDisposable
{
    private readonly WinForms.NotifyIcon _tray = new();

    private readonly System.Drawing.Icon _iconApp;
    private readonly System.Drawing.Icon _iconMuted;
    private readonly System.Drawing.Icon _iconUnmuted;
    private readonly System.Drawing.Icon _iconTalking;

    public event Action? OpenRequested;
    public event Action? MuteToggled;
    public event Action? ExitRequested;

    public TrayManager()
    {
        _iconApp     = LoadIcon("app.ico")     ?? System.Drawing.SystemIcons.Application;
        _iconMuted   = LoadIcon("muted.ico")   ?? System.Drawing.SystemIcons.Shield;
        _iconUnmuted = LoadIcon("unmuted.ico") ?? System.Drawing.SystemIcons.Information;
        _iconTalking = LoadIcon("talking.ico") ?? System.Drawing.SystemIcons.Exclamation;
    }

    private static System.Drawing.Icon? LoadIcon(string filename)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", filename);
        return File.Exists(path) ? new System.Drawing.Icon(path) : null;
    }

    public void Initialize()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open Cordex", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Toggle Mute",   null, (_, _) => MuteToggled?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit",          null, (_, _) => ExitRequested?.Invoke());

        _tray.Icon             = _iconApp;
        _tray.Text             = "Cordex";
        _tray.ContextMenuStrip = menu;
        _tray.Visible          = true;
        _tray.DoubleClick     += (_, _) => OpenRequested?.Invoke();
    }

    public void SetState(MuteState state)
    {
        (_tray.Icon, _tray.Text) = state switch
        {
            MuteState.Muted   => (_iconMuted,   "Cordex - Muted"),
            MuteState.Unmuted => (_iconUnmuted, "Cordex - Unmuted"),
            MuteState.Talking => (_iconTalking, "Cordex - Talking"),
            _                 => (_iconApp,     "Cordex"),
        };
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
    }
}
