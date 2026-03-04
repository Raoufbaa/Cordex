using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Cordex.Core;

public class KeybindManager
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey  (IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY  = 0x0312;
    private const int ID_MUTE    = 1;
    private const int ID_DEAFEN  = 2;
    private const int ID_FOCUS   = 3;

    private IntPtr      _hwnd;
    private HwndSource? _source;

    public event Action? ToggleMute;
    public event Action? ToggleDeafen;
    public event Action? FocusWindow;

    public void Register(Window window)
    {
        _hwnd   = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        var s = SettingsManager.Current;
        RegisterHotKey(_hwnd, ID_MUTE,   s.Mute.Modifiers,   s.Mute.VirtualKey);
        RegisterHotKey(_hwnd, ID_DEAFEN, s.Deafen.Modifiers, s.Deafen.VirtualKey);
        RegisterHotKey(_hwnd, ID_FOCUS,  s.Focus.Modifiers,  s.Focus.VirtualKey);
    }

    public void Unregister()
    {
        UnregisterHotKey(_hwnd, ID_MUTE);
        UnregisterHotKey(_hwnd, ID_DEAFEN);
        UnregisterHotKey(_hwnd, ID_FOCUS);
        _source?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            handled = true;
            switch (wParam.ToInt32())
            {
                case ID_MUTE:   ToggleMute?.Invoke();   break;
                case ID_DEAFEN: ToggleDeafen?.Invoke(); break;
                case ID_FOCUS:  FocusWindow?.Invoke();  break;
            }
        }
        return IntPtr.Zero;
    }
}
