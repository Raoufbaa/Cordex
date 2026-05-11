using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Cordex.Core;

/// <summary>
/// Manages Windows microphone Automatic Gain Control (AGC) behaviour.
///
/// Windows applies two layers of automatic volume adjustment when an
/// application uses a microphone in communications mode:
///
///   1. WebRTC software AGC — Chromium's own gain-control pipeline
///      (disabled by injecting autoGainControl:false in getUserMedia).
///
///   2. Windows Communications-role session ducking — when any app opens
///      a capture session in eCommunications role, Windows automatically
///      ducks (lowers) other render streams and may apply system-effects
///      (SysFX) APO gain processing on the capture endpoint.
///
/// This class handles layer 2 by:
///   • Calling IAudioSessionControl2.SetDuckingPreference(true) on the
///     default render endpoint's session manager so Windows stops
///     automatically lowering other app volumes during a call.
///   • Optionally disabling system-effects (SysFX) on the default capture
///     endpoint via the device IPropertyStore so the driver-level AGC /
///     noise-suppression chain is bypassed entirely.
///
/// All COM work is marshalled off the UI thread.
/// </summary>
public static class MicAgcManager
{
    // ── DEVPKEY_AudioEndpoint_Disable_SysFX ─────────────────────────────────
    // {1da5d803-d492-4edd-8c23-e0c0ffee7f0e}, 5
    private static readonly Guid DEVPKEY_AudioEndpoint_Disable_SysFX_FmtId =
        new Guid("{1da5d803-d492-4edd-8c23-e0c0ffee7f0e}");
    private const uint DEVPKEY_AudioEndpoint_Disable_SysFX_Pid = 5;

    // PROPVARIANT vt values
    private const ushort VT_EMPTY = 0;
    private const ushort VT_BOOL  = 11;

    private static bool _duckingDisabled = false;
    private static bool _sysFxDisabled   = false;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply the current settings (call after settings are saved / on app start).
    /// Runs all COM work on a background thread.
    /// </summary>
    public static void Apply()
    {
        Task.Run(() =>
        {
            try
            {
                ApplyDuckingPreference(SettingsManager.Current.DisableWinDucking);
                ApplySysFxSetting(SettingsManager.Current.DisableAudioEnhancements);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MicAgcManager] Apply failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Restore everything to Windows defaults. Called on app exit.
    /// </summary>
    public static void Restore()
    {
        try
        {
            if (_duckingDisabled) ApplyDuckingPreference(false);
            if (_sysFxDisabled)   ApplySysFxSetting(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MicAgcManager] Restore failed: {ex.Message}");
        }
    }

    // ── Communications ducking ────────────────────────────────────────────────

    /// <summary>
    /// When enabled=true, tells Windows NOT to duck other app volumes
    /// when a communications-role capture session is active.
    /// </summary>
    private static void ApplyDuckingPreference(bool disableDucking)
    {
        try
        {
            // Get the default render endpoint (output device).
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            var hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eCommunications, out var device);
            if (hr != 0 || device == null)
            {
                Debug.WriteLine("[MicAgcManager] Could not get render endpoint for ducking control");
                return;
            }

            // Activate IAudioSessionManager2
            var iid = typeof(IAudioSessionManager2).GUID;
            hr = device.Activate(ref iid, 0, IntPtr.Zero, out var obj);
            if (hr != 0 || obj is not IAudioSessionManager2 mgr)
            {
                Debug.WriteLine("[MicAgcManager] Could not activate IAudioSessionManager2");
                return;
            }

            // Get session enumerator
            hr = mgr.GetSessionEnumerator(out var sessionEnum);
            if (hr != 0 || sessionEnum == null)
            {
                Debug.WriteLine("[MicAgcManager] Could not get session enumerator");
                return;
            }

            hr = sessionEnum.GetCount(out var count);
            if (hr != 0) return;

            int changed = 0;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    hr = sessionEnum.GetSession(i, out var ctrl);
                    if (hr != 0 || ctrl == null) continue;

                    if (ctrl is IAudioSessionControl2 ctrl2)
                    {
                        // SetDuckingPreference(true) = "I don't want to be ducked"
                        // SetDuckingPreference(false) = restore Windows default ducking
                        ctrl2.SetDuckingPreference(disableDucking);
                        changed++;
                    }
                }
                catch { /* skip inaccessible sessions */ }
            }

            _duckingDisabled = disableDucking;
            Debug.WriteLine($"[MicAgcManager] Ducking preference set to optOut={disableDucking} on {changed} sessions");

            Marshal.ReleaseComObject(sessionEnum);
            Marshal.ReleaseComObject(mgr);
            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MicAgcManager] ApplyDuckingPreference error: {ex.Message}");
        }
    }

    // ── System Effects (SysFX) / Driver AGC ───────────────────────────────────

    /// <summary>
    /// Disables or re-enables all system-effects (noise suppression, AGC,
    /// echo cancellation, microphone boost) on the default capture endpoint
    /// by writing DEVPKEY_AudioEndpoint_Disable_SysFX via the device's
    /// IPropertyStore (opened in ReadWrite mode, requires no elevation on
    /// modern Windows 10/11 for user-owned devices).
    /// </summary>
    private static void ApplySysFxSetting(bool disable)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();

            // Try eCommunications role first, fall back to eConsole
            IMMDevice? device = null;
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out device);
            if (hr != 0 || device == null)
                hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out device);
            if (hr != 0 || device == null)
            {
                Debug.WriteLine("[MicAgcManager] Could not get capture endpoint for SysFX control");
                return;
            }

            // STGM_READWRITE = 2
            hr = device.OpenPropertyStore(2, out var store);
            if (hr != 0 || store == null)
            {
                Debug.WriteLine("[MicAgcManager] OpenPropertyStore failed — may need elevation");
                // Don't propagate — just silently skip
                return;
            }

            var key = new PROPERTYKEY
            {
                fmtid = DEVPKEY_AudioEndpoint_Disable_SysFX_FmtId,
                pid    = DEVPKEY_AudioEndpoint_Disable_SysFX_Pid
            };

            // Build PROPVARIANT for VT_BOOL (VARIANT_TRUE = -1, VARIANT_FALSE = 0)
            var pv = new PROPVARIANT
            {
                vt    = VT_BOOL,
                boolVal = (short)(disable ? -1 : 0)
            };

            hr = store.SetValue(ref key, ref pv);
            if (hr == 0)
            {
                _sysFxDisabled = disable;
                Debug.WriteLine($"[MicAgcManager] SysFX on capture endpoint {(disable ? "disabled" : "enabled")}");
            }
            else
            {
                Debug.WriteLine($"[MicAgcManager] SetValue for SysFX returned hr=0x{hr:X8} — may need elevation");
            }

            Marshal.ReleaseComObject(store);
            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex)
        {
            // Non-fatal — JS layer still works independently
            Debug.WriteLine($"[MicAgcManager] ApplySysFxSetting error: {ex.Message}");
        }
    }

    // ── COM Interfaces ─────────────────────────────────────────────────────────

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx,
            IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? ppInterface);

        [PreserveSig]
        int OpenPropertyStore(uint stgmAccess, out IPropertyStore? ppProperties);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int NotImpl1();
        int NotImpl2();
        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator? ppSessionList);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int SessionCount);
        [PreserveSig] int GetSession(int SessionIndex,
            [MarshalAs(UnmanagedType.Interface)] out IAudioSessionControl? Session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        int NotImpl1(); int NotImpl2(); int NotImpl3(); int NotImpl4();
        int NotImpl5(); int NotImpl6(); int NotImpl7(); int NotImpl8();
        int NotImpl9();
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl methods (inherited, skip 9)
        int NotImpl1(); int NotImpl2(); int NotImpl3(); int NotImpl4();
        int NotImpl5(); int NotImpl6(); int NotImpl7(); int NotImpl8();
        int NotImpl9();
        // IAudioSessionControl2 methods
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int GetSessionIdentifier     ([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int GetProcessId             (out uint pRetVal);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)]  public ushort vt;
        [FieldOffset(2)]  public ushort wReserved1;
        [FieldOffset(4)]  public ushort wReserved2;
        [FieldOffset(6)]  public ushort wReserved3;
        [FieldOffset(8)]  public short  boolVal;
        [FieldOffset(8)]  public IntPtr pVal;
    }

    private enum EDataFlow { eRender, eCapture, eAll }
    private enum ERole     { eConsole, eMultimedia, eCommunications }
}
