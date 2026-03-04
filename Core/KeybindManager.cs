using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nextcord.Core;

public class KeybindManager
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey  (IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int  WM_HOTKEY = 0x0312;
    private const uint MOD_CTRL  = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_M      = 0x4D;
    private const uint VK_D      = 0x44;
    private const uint VK_N      = 0x4E;
    private const int  ID_MUTE   = 1;
    private const int  ID_DEAFEN = 2;
    private const int  ID_FOCUS  = 3;

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

        RegisterHotKey(_hwnd, ID_MUTE,   MOD_CTRL | MOD_SHIFT, VK_M);
        RegisterHotKey(_hwnd, ID_DEAFEN, MOD_CTRL | MOD_SHIFT, VK_D);
        RegisterHotKey(_hwnd, ID_FOCUS,  MOD_CTRL | MOD_SHIFT, VK_N);
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
