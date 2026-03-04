using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cordex.Core;

public class AudioMonitor : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioMeterInformation? _meterInfo;
    
    private const float VoiceThreshold = 0.02f; // 2% threshold
    private const int CheckIntervalMs = 50;
    private const int TalkingDebounceMs = 200;
    
    public event Action<bool>? VoiceActivityChanged;
    
    private bool _isTalking;
    private bool _isRunning;
    private DateTime _lastVoiceDetected = DateTime.MinValue;

    public AudioMonitor()
    {
        _timer = new System.Threading.Timer(CheckAudioLevel, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_isRunning) return;
        
        try
        {
            _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            
            // Try to get default capture device
            int hr = _deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out _device);
            
            if (hr != 0 || _device == null) return;
            
            var iid = typeof(IAudioMeterInformation).GUID;
            object? obj = null;
            hr = _device.Activate(ref iid, 0, IntPtr.Zero, out obj);
            
            if (hr != 0 || obj == null) return;
            
            _meterInfo = obj as IAudioMeterInformation;
            
            if (_meterInfo == null) return;
            
            _isRunning = true;
            _timer.Change(0, CheckIntervalMs);
        }
        catch { }
    }

    public void Stop()
    {
        _isRunning = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        
        // Reset state
        if (_isTalking)
        {
            _isTalking = false;
            VoiceActivityChanged?.Invoke(false);
        }
        
        Cleanup();
    }

    private void CheckAudioLevel(object? state)
    {
        if (!_isRunning || _meterInfo == null) return;

        try
        {
            int hr = _meterInfo.GetPeakValue(out float peak);
            
            if (hr != 0) return;
            
            // Detect voice activity
            if (peak > VoiceThreshold)
            {
                _lastVoiceDetected = DateTime.Now;
                
                if (!_isTalking)
                {
                    _isTalking = true;
                    VoiceActivityChanged?.Invoke(true);
                }
            }
            else
            {
                // Keep talking state for debounce period after voice stops
                var timeSinceLastVoice = (DateTime.Now - _lastVoiceDetected).TotalMilliseconds;
                
                if (_isTalking && timeSinceLastVoice > TalkingDebounceMs)
                {
                    _isTalking = false;
                    VoiceActivityChanged?.Invoke(false);
                }
            }
        }
        catch { }
    }

    private void Cleanup()
    {
        try
        {
            if (_meterInfo != null)
            {
                Marshal.ReleaseComObject(_meterInfo);
                _meterInfo = null;
            }
            
            if (_device != null)
            {
                Marshal.ReleaseComObject(_device);
                _device = null;
            }
            
            if (_deviceEnumerator != null)
            {
                Marshal.ReleaseComObject(_deviceEnumerator);
                _deviceEnumerator = null;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}

// ── COM Interfaces ───────────────────────────────────────────────────────────

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int NotImpl1();
    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? ppDevice);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? ppInterface);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig]
    int GetPeakValue(out float pfPeak);
}

internal enum EDataFlow
{
    eRender,
    eCapture,
    eAll
}

internal enum ERole
{
    eConsole,
    eMultimedia,
    eCommunications
}
