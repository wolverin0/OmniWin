using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

#region Models

public class AppAudioSession : INotifyPropertyChanged
{
    private float _volume;
    private bool _isMuted;
    private string _processName = string.Empty;
    private string _displayName = string.Empty;

    public int ProcessId { get; set; }
    public int Pid => ProcessId;

    public string ProcessName
    {
        get => _processName;
        set
        {
            if (_processName != value)
            {
                _processName = value;
                OnPropertyChanged(nameof(ProcessName));
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName != value)
            {
                _displayName = value;
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            float clamped = Math.Clamp(value, 0.0f, 1.0f);
            if (Math.Abs(_volume - clamped) > 0.0001f)
            {
                _volume = clamped;
                OnPropertyChanged(nameof(Volume));
                OnPropertyChanged(nameof(VolumePercent));
                OnPropertyChanged(nameof(VolumePercentText));
            }
        }
    }

    public int VolumePercent
    {
        get => (int)Math.Round(_volume * 100f);
        set
        {
            Volume = Math.Clamp(value / 100f, 0.0f, 1.0f);
        }
    }

    public string VolumePercentText => $"{VolumePercent}%";

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted != value)
            {
                _isMuted = value;
                OnPropertyChanged(nameof(IsMuted));
                OnPropertyChanged(nameof(MuteIcon));
            }
        }
    }

    public string MuteIcon => IsMuted ? "🔇" : "🔊";

    public string? ProcessPath { get; set; }
    public bool IsSystemSounds { get; set; }
    public AudioSessionState State { get; set; }

    private float _peakValue;
    public float PeakValue
    {
        get => _peakValue;
        set
        {
            float clamped = Math.Clamp(value, 0.0f, 1.0f);
            if (Math.Abs(_peakValue - clamped) > 0.005f)
            {
                _peakValue = clamped;
                OnPropertyChanged(nameof(PeakValue));
                OnPropertyChanged(nameof(PeakPercent));
            }
        }
    }
    public int PeakPercent => (int)Math.Round(_peakValue * 100f);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

#endregion

#region COM Interop Enums & Interfaces

public enum AudioSessionState
{
    AudioSessionStateInactive = 0,
    AudioSessionStateActive = 1,
    AudioSessionStateExpired = 2
}

internal enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

[Flags]
internal enum CLSCTX : uint
{
    INPROC_SERVER = 0x1,
    INPROC_HANDLER = 0x2,
    LOCAL_SERVER = 0x4,
    REMOTE_SERVER = 0x10,
    ALL = INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr pClient);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    [PreserveSig]
    int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

    [PreserveSig]
    int GetState(out uint pdwState);
}

[ComImport]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig]
    int GetPeakValue(out float pfPeak);

    [PreserveSig]
    int GetMeteringChannelCount(out uint pnChannelCount);

    [PreserveSig]
    int GetChannelsPeakValues(uint u32ChannelCount, [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] afPeakValues);

    [PreserveSig]
    int QueryHardwareSupport(out uint pdwHardwareSupportMask);
}

[ComImport]
[Guid("77AA99A0-1BD6-440F-8BE0-5D1A444C3FEE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    [PreserveSig]
    int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr AudioVolume);

    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);

    [PreserveSig]
    int RegisterSessionNotification(IntPtr NewSessionNotifications);

    [PreserveSig]
    int UnregisterSessionNotification(IntPtr NewSessionNotifications);

    [PreserveSig]
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionID, IntPtr duckNotification);

    [PreserveSig]
    int UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport]
[Guid("E2F5E0F1-0775-40DD-B99C-324E26AAB1E1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int SessionCount);

    [PreserveSig]
    int GetSession(int SessionIndex, out IAudioSessionControl Session);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig]
    int GetState(out AudioSessionState pRetVal);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid pRetVal);

    [PreserveSig]
    int SetGroupingParam(ref Guid Override, ref Guid EventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IntPtr NewNotifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IntPtr NewNotifications);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA8-8809DCBC9223")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // Inherited from IAudioSessionControl
    [PreserveSig]
    int GetState(out AudioSessionState pRetVal);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid pRetVal);

    [PreserveSig]
    int SetGroupingParam(ref Guid Override, ref Guid EventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IntPtr NewNotifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IntPtr NewNotifications);

    // IAudioSessionControl2 additions
    [PreserveSig]
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int GetProcessId(out uint pRetVal);

    [PreserveSig]
    int IsSystemSoundsSession();

    [PreserveSig]
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float fLevel, ref Guid EventContext);

    [PreserveSig]
    int GetMasterVolume(out float pfLevel);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid EventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IntPtr pNotify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IntPtr pNotify);

    [PreserveSig]
    int GetChannelCount(out uint pnChannelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float pfLevelDB);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float pfLevel);

    [PreserveSig]
    int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);

    [PreserveSig]
    int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);

    [PreserveSig]
    int VolumeStepUp(ref Guid pguidEventContext);

    [PreserveSig]
    int VolumeStepDown(ref Guid pguidEventContext);

    [PreserveSig]
    int QueryHardwareSupport(out uint pdwHardwareSupportMask);

    [PreserveSig]
    int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}

#endregion

public class AudioMixerService
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioSessionManager2 = new Guid("77AA99A0-1BD6-440F-8BE0-5D1A444C3FEE");
    private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

    private IMMDeviceEnumerator? CreateDeviceEnumerator()
    {
        try
        {
            Type? type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
            if (type == null) return null;
            return Activator.CreateInstance(type) as IMMDeviceEnumerator;
        }
        catch
        {
            return null;
        }
    }

    private IMMDevice? GetDefaultRenderDevice(IMMDeviceEnumerator enumerator)
    {
        try
        {
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice device);
            if (hr != 0 || device == null)
            {
                hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out device);
            }
            return hr == 0 ? device : null;
        }
        catch
        {
            return null;
        }
    }

    private IAudioSessionManager2? GetSessionManager(IMMDevice device)
    {
        try
        {
            Guid iid = IID_IAudioSessionManager2;
            int hr = device.Activate(ref iid, CLSCTX.ALL, IntPtr.Zero, out object obj);
            return hr == 0 ? obj as IAudioSessionManager2 : null;
        }
        catch
        {
            return null;
        }
    }

    private IAudioEndpointVolume? GetEndpointVolume(IMMDevice device)
    {
        try
        {
            Guid iid = IID_IAudioEndpointVolume;
            int hr = device.Activate(ref iid, CLSCTX.ALL, IntPtr.Zero, out object obj);
            return hr == 0 ? obj as IAudioEndpointVolume : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Obtiene cada sesión de audio activa con PID, nombre de proceso, volumen actual (0-100%) y si está silenciado.
    /// Deduplica múltiples flujos pertenecientes al mismo proceso para interfaz estilo EarTrumpet.
    /// </summary>
    public List<AppAudioSession> GetAudioSessions()
    {
        var sessions = new List<AppAudioSession>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnum = null;

        try
        {
            enumerator = CreateDeviceEnumerator();
            if (enumerator == null) return sessions;

            device = GetDefaultRenderDevice(enumerator);
            if (device == null) return sessions;

            sessionManager = GetSessionManager(device);
            if (sessionManager == null) return sessions;

            int hr = sessionManager.GetSessionEnumerator(out sessionEnum);
            if (hr != 0 || sessionEnum == null) return sessions;

            sessionEnum.GetCount(out int count);
            var seenPids = new HashSet<int>();

            for (int i = 0; i < count; i++)
            {
                IAudioSessionControl? sessionControl = null;
                try
                {
                    int hrSession = sessionEnum.GetSession(i, out sessionControl);
                    if (hrSession != 0 || sessionControl == null) continue;

                    var control2 = sessionControl as IAudioSessionControl2;
                    var simpleVolume = sessionControl as ISimpleAudioVolume;
                    if (control2 == null || simpleVolume == null) continue;

                    control2.GetState(out AudioSessionState state);
                    if (state == AudioSessionState.AudioSessionStateExpired)
                        continue;

                    control2.GetProcessId(out uint rawPid);
                    bool isSystem = (control2.IsSystemSoundsSession() == 0) || rawPid == 0;
                    int pid = isSystem ? 0 : (int)rawPid;

                    // Deduplicar si el mismo proceso tiene varias sesiones de audio
                    if (seenPids.Contains(pid))
                        continue;

                    simpleVolume.GetMasterVolume(out float volume);
                    simpleVolume.GetMute(out bool isMuted);

                    string processName;
                    string displayName;
                    string? processPath = null;

                    if (isSystem)
                    {
                        processName = "Sonidos del Sistema";
                        displayName = "Sonidos del Sistema de Windows";
                    }
                    else
                    {
                        try
                        {
                            using var proc = Process.GetProcessById(pid);
                            processName = proc.ProcessName + ".exe";
                            displayName = !string.IsNullOrWhiteSpace(proc.MainWindowTitle)
                                ? proc.MainWindowTitle
                                : proc.ProcessName;

                            try
                            {
                                processPath = proc.MainModule?.FileName;
                            }
                            catch
                            {
                                // Acceso denegado en procesos de sistema o arquitecturas mixtas
                            }
                        }
                        catch
                        {
                            control2.GetDisplayName(out string comName);
                            if (!string.IsNullOrWhiteSpace(comName))
                            {
                                processName = comName;
                                displayName = comName;
                            }
                            else
                            {
                                processName = $"Proceso #{pid}";
                                displayName = $"PID {pid}";
                            }
                        }
                    }

                    seenPids.Add(pid);
                    float peak = 0f;
                    if (sessionControl is IAudioMeterInformation meter)
                    {
                        try { meter.GetPeakValue(out peak); } catch { }
                    }

                    sessions.Add(new AppAudioSession
                    {
                        ProcessId = pid,
                        ProcessName = processName,
                        DisplayName = displayName,
                        Volume = Math.Clamp(volume, 0.0f, 1.0f),
                        IsMuted = isMuted,
                        ProcessPath = processPath,
                        IsSystemSounds = isSystem,
                        State = state,
                        PeakValue = Math.Clamp(peak, 0.0f, 1.0f)
                    });
                }
                catch
                {
                    // Manejo seguro de excepciones si la aplicación cierra su sesión concurrentemente
                }
                finally
                {
                    if (sessionControl != null && Marshal.IsComObject(sessionControl))
                    {
                        Marshal.ReleaseComObject(sessionControl);
                    }
                }
            }
        }
        catch
        {
            // Falla general de subsistema de audio manejada de forma segura
        }
        finally
        {
            if (sessionEnum != null && Marshal.IsComObject(sessionEnum))
                Marshal.ReleaseComObject(sessionEnum);
            if (sessionManager != null && Marshal.IsComObject(sessionManager))
                Marshal.ReleaseComObject(sessionManager);
            if (device != null && Marshal.IsComObject(device))
                Marshal.ReleaseComObject(device);
            if (enumerator != null && Marshal.IsComObject(enumerator))
                Marshal.ReleaseComObject(enumerator);
        }

        return sessions;
    }

    /// <summary>
    /// Ajusta el volumen individual (0.0f a 1.0f) para el proceso dado.
    /// </summary>
    public void SetVolume(int pid, float volume)
    {
        float clamped = Math.Clamp(volume, 0.0f, 1.0f);
        ExecuteSessionAction(pid, simpleVolume =>
        {
            Guid empty = Guid.Empty;
            simpleVolume.SetMasterVolume(clamped, ref empty);
        });
    }

    /// <summary>
    /// Silencia o reactiva el audio de un proceso individual.
    /// </summary>
    public void SetMute(int pid, bool mute)
    {
        ExecuteSessionAction(pid, simpleVolume =>
        {
            Guid empty = Guid.Empty;
            simpleVolume.SetMute(mute, ref empty);
        });
    }

    /// <summary>
    /// Obtiene el volumen maestro del endpoint de audio predeterminado (0.0f a 1.0f).
    /// </summary>
    public float GetMasterVolume()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;
        try
        {
            enumerator = CreateDeviceEnumerator();
            if (enumerator == null) return 1.0f;

            device = GetDefaultRenderDevice(enumerator);
            if (device == null) return 1.0f;

            endpoint = GetEndpointVolume(device);
            if (endpoint == null) return 1.0f;

            int hr = endpoint.GetMasterVolumeLevelScalar(out float level);
            return hr == 0 ? Math.Clamp(level, 0.0f, 1.0f) : 1.0f;
        }
        catch
        {
            return 1.0f;
        }
        finally
        {
            if (endpoint != null && Marshal.IsComObject(endpoint))
                Marshal.ReleaseComObject(endpoint);
            if (device != null && Marshal.IsComObject(device))
                Marshal.ReleaseComObject(device);
            if (enumerator != null && Marshal.IsComObject(enumerator))
                Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Obtiene el nivel pico de audio maestro actual (0.0f a 1.0f para Vúmetro).
    /// </summary>
    public float GetMasterPeakValue()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = CreateDeviceEnumerator();
            if (enumerator == null) return 0f;

            device = GetDefaultRenderDevice(enumerator);
            if (device == null) return 0f;

            var iid = typeof(IAudioMeterInformation).GUID;
            int hr = device.Activate(ref iid, CLSCTX.ALL, IntPtr.Zero, out object meterObj);
            if (hr == 0 && meterObj is IAudioMeterInformation meter)
            {
                meter.GetPeakValue(out float peak);
                return Math.Clamp(peak, 0.0f, 1.0f);
            }
        }
        catch { }
        finally
        {
            if (device != null && Marshal.IsComObject(device))
                Marshal.ReleaseComObject(device);
            if (enumerator != null && Marshal.IsComObject(enumerator))
                Marshal.ReleaseComObject(enumerator);
        }
        return 0f;
    }

    /// <summary>
    /// Establece el volumen maestro del endpoint de audio predeterminado (0.0f a 1.0f).
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        float clamped = Math.Clamp(volume, 0.0f, 1.0f);
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;
        try
        {
            enumerator = CreateDeviceEnumerator();
            if (enumerator == null) return;

            device = GetDefaultRenderDevice(enumerator);
            if (device == null) return;

            endpoint = GetEndpointVolume(device);
            if (endpoint == null) return;

            Guid empty = Guid.Empty;
            endpoint.SetMasterVolumeLevelScalar(clamped, ref empty);
        }
        catch
        {
        }
        finally
        {
            if (endpoint != null && Marshal.IsComObject(endpoint))
                Marshal.ReleaseComObject(endpoint);
            if (device != null && Marshal.IsComObject(device))
                Marshal.ReleaseComObject(device);
            if (enumerator != null && Marshal.IsComObject(enumerator))
                Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Obtiene si el endpoint maestro de reproducción está silenciado.
    /// </summary>
    public bool GetMasterMute()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;
        try
        {
            enumerator = CreateDeviceEnumerator();
            if (enumerator == null) return false;

            device = GetDefaultRenderDevice(enumerator);
            if (device == null) return false;

            endpoint = GetEndpointVolume(device);
            if (endpoint == null) return false;

            endpoint.GetMute(out bool isMuted);
            return isMuted;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (endpoint != null && Marshal.IsComObject(endpoint))
                Marshal.ReleaseComObject(endpoint);
            if (device != null && Marshal.IsComObject(device))
                Marshal.ReleaseComObject(device);
            if (enumerator != null && Marshal.IsComObject(enumerator))
                Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Establece el estado silenciado en el endpoint maestro de reproducción.
    /// </summary>
    public void SetMasterMute(bool mute)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;
        try
        {
            enumerator = CreateDeviceEnumerator();
            if (enumerator == null) return;

            device = GetDefaultRenderDevice(enumerator);
            if (device == null) return;

            endpoint = GetEndpointVolume(device);
            if (endpoint == null) return;

            Guid empty = Guid.Empty;
            endpoint.SetMute(mute, ref empty);
        }
        catch
        {
        }
        finally
        {
            if (endpoint != null && Marshal.IsComObject(endpoint))
                Marshal.ReleaseComObject(endpoint);
            if (device != null && Marshal.IsComObject(device))
                Marshal.ReleaseComObject(device);
            if (enumerator != null && Marshal.IsComObject(enumerator))
                Marshal.ReleaseComObject(enumerator);
        }
    }

    private void ExecuteSessionAction(int targetPid, Action<ISimpleAudioVolume> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnum = null;

        try
        {
            enumerator = CreateDeviceEnumerator();
            if (enumerator == null) return;

            device = GetDefaultRenderDevice(enumerator);
            if (device == null) return;

            sessionManager = GetSessionManager(device);
            if (sessionManager == null) return;

            int hr = sessionManager.GetSessionEnumerator(out sessionEnum);
            if (hr != 0 || sessionEnum == null) return;

            sessionEnum.GetCount(out int count);

            for (int i = 0; i < count; i++)
            {
                IAudioSessionControl? sessionControl = null;
                try
                {
                    int hrSession = sessionEnum.GetSession(i, out sessionControl);
                    if (hrSession != 0 || sessionControl == null) continue;

                    var control2 = sessionControl as IAudioSessionControl2;
                    var simpleVolume = sessionControl as ISimpleAudioVolume;
                    if (control2 == null || simpleVolume == null) continue;

                    control2.GetProcessId(out uint rawPid);
                    bool isSystem = (control2.IsSystemSoundsSession() == 0) || rawPid == 0;
                    int pid = isSystem ? 0 : (int)rawPid;

                    if ((targetPid == 0 && isSystem) || pid == targetPid)
                    {
                        action(simpleVolume);
                    }
                }
                catch
                {
                    // Manejo seguro si la sesión se destruye mientras se envía la acción
                }
                finally
                {
                    if (sessionControl != null && Marshal.IsComObject(sessionControl))
                    {
                        Marshal.ReleaseComObject(sessionControl);
                    }
                }
            }
        }
        catch
        {
            // Manejo seguro general
        }
        finally
        {
            if (sessionEnum != null && Marshal.IsComObject(sessionEnum))
                Marshal.ReleaseComObject(sessionEnum);
            if (sessionManager != null && Marshal.IsComObject(sessionManager))
                Marshal.ReleaseComObject(sessionManager);
            if (device != null && Marshal.IsComObject(device))
                Marshal.ReleaseComObject(device);
            if (enumerator != null && Marshal.IsComObject(enumerator))
                Marshal.ReleaseComObject(enumerator);
        }
    }
}
