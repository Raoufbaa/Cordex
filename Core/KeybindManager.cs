using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Diagnostics;

namespace Cordex.Core;

public class KeybindManager
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int ID_MUTE = 1;
    private const int ID_DEAFEN = 2;
    private const int ID_FOCUS = 3;

    // MOD_NOREPEAT prevents key repeat
    private const uint MOD_NOREPEAT = 0x4000;

    private IntPtr _hwnd;
    private HwndSource? _source;

    public event Action? ToggleMute;
    public event Action? ToggleDeafen;
    public event Action? FocusWindow;

    public void Register(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        var s = SettingsManager.Current;

        // Register hotkeys with MOD_NOREPEAT
        bool muteOk = RegisterHotKey(_hwnd, ID_MUTE, s.Mute.Modifiers | MOD_NOREPEAT, s.Mute.VirtualKey);
        bool deafenOk = RegisterHotKey(_hwnd, ID_DEAFEN, s.Deafen.Modifiers | MOD_NOREPEAT, s.Deafen.VirtualKey);
        bool focusOk = RegisterHotKey(_hwnd, ID_FOCUS, s.Focus.Modifiers | MOD_NOREPEAT, s.Focus.VirtualKey);

        Debug.WriteLine($"[Keybind] Mute registered: {muteOk} (Mods: 0x{s.Mute.Modifiers:X}, VK: 0x{s.Mute.VirtualKey:X})");
        Debug.WriteLine($"[Keybind] Deafen registered: {deafenOk} (Mods: 0x{s.Deafen.Modifiers:X}, VK: 0x{s.Deafen.VirtualKey:X})");
        Debug.WriteLine($"[Keybind] Focus registered: {focusOk} (Mods: 0x{s.Focus.Modifiers:X}, VK: 0x{s.Focus.VirtualKey:X})");
    }

    public void Unregister()
    {
        UnregisterHotKey(_hwnd, ID_MUTE);
        UnregisterHotKey(_hwnd, ID_DEAFEN);
        UnregisterHotKey(_hwnd, ID_FOCUS);
        _source?.RemoveHook(WndProc);
        Debug.WriteLine("[Keybind] All hotkeys unregistered");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            Debug.WriteLine($"[Keybind] Hotkey triggered: ID={id}");
            
            handled = true;
            switch (id)
            {
                case ID_MUTE:
                    Debug.WriteLine("[Keybind] Mute triggered");
                    ToggleMute?.Invoke();
                    break;
                case ID_DEAFEN:
                    Debug.WriteLine("[Keybind] Deafen triggered");
                    ToggleDeafen?.Invoke();
                    break;
                case ID_FOCUS:
                    Debug.WriteLine("[Keybind] Focus triggered");
                    FocusWindow?.Invoke();
                    break;
            }
        }
        return IntPtr.Zero;
    }
}
