using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class AudioMixerControl : UserControl
{
    private readonly AudioMixerService _mixerService = new();
    private readonly ObservableCollection<AudioMixerItemViewModel> _sessionItems = new();
    private readonly Dictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _refreshTimer;
    private bool _isUpdatingMaster;
    private bool _isMasterMuted;

    public AudioMixerControl()
    {
        InitializeComponent();
        ItemsAudioSessions.ItemsSource = _sessionItems;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateMasterVolumeUI();
        RefreshSessions();

        // Polling para refresco automático de sesiones estilo EarTrumpet
        if (_refreshTimer == null)
        {
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _refreshTimer.Tick += (s, args) => RefreshSessions(silent: true);
        }
        _refreshTimer.Start();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer?.Stop();
    }

    private void UpdateMasterVolumeUI()
    {
        try
        {
            _isUpdatingMaster = true;
            float masterVol = _mixerService.GetMasterVolume();
            _isMasterMuted = _mixerService.GetMasterMute();

            MasterSlider.Value = Math.Round(masterVol * 100f);
            TxtMasterVolumePercent.Text = $"{(int)MasterSlider.Value}%";
            BtnMasterMute.Content = _isMasterMuted ? "🔇" : "🔊";
        }
        catch
        {
            // Silencioso ante excepciones de dispositivo
        }
        finally
        {
            _isUpdatingMaster = false;
        }
    }

    private void RefreshSessions(bool silent = false)
    {
        try
        {
            if (!silent)
            {
                UpdateMasterVolumeUI();
            }

            var freshSessions = _mixerService.GetAudioSessions();

            // Sincronizar colección sin recrear elementos para que no parpadee
            var activePids = new HashSet<int>(freshSessions.Select(s => s.ProcessId));

            // Eliminar sesiones que ya no existen
            for (int i = _sessionItems.Count - 1; i >= 0; i--)
            {
                if (!activePids.Contains(_sessionItems[i].ProcessId))
                {
                    _sessionItems.RemoveAt(i);
                }
            }

            // Actualizar existentes o agregar nuevas
            foreach (var session in freshSessions)
            {
                var existing = _sessionItems.FirstOrDefault(item => item.ProcessId == session.ProcessId);
                if (existing != null)
                {
                    existing.UpdateFromSession(session);
                }
                else
                {
                    ImageSource? icon = GetOrLoadProcessIcon(session.ProcessPath, session.ProcessName);
                    _sessionItems.Add(new AudioMixerItemViewModel(session, _mixerService, icon));
                }
            }

            // Actualizar contadores y visibilidad de estado vacío
            TxtSessionCount.Text = _sessionItems.Count == 1
                ? "1 Aplicación con Audio"
                : $"{_sessionItems.Count} Aplicaciones con Audio";

            BorderEmptyState.Visibility = _sessionItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            // Protección de interfaz de usuario
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshSessions();
    }

    private void MasterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingMaster) return;

        TxtMasterVolumePercent.Text = $"{(int)e.NewValue}%";
        float vol = (float)(e.NewValue / 100.0);
        _mixerService.SetMasterVolume(vol);

        // Si se sube el volumen y estaba silenciado, desilenciar visualmente
        if (vol > 0.01f && _isMasterMuted)
        {
            _isMasterMuted = false;
            _mixerService.SetMasterMute(false);
            BtnMasterMute.Content = "🔊";
        }
    }

    private void BtnMasterMute_Click(object sender, RoutedEventArgs e)
    {
        _isMasterMuted = !_isMasterMuted;
        _mixerService.SetMasterMute(_isMasterMuted);
        BtnMasterMute.Content = _isMasterMuted ? "🔇" : "🔊";
    }

    private void BtnMuteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AudioMixerItemViewModel itemVm)
        {
            itemVm.ToggleMute();
        }
    }

    #region Icon Extraction Helper

    private ImageSource? GetOrLoadProcessIcon(string? path, string processName)
    {
        string cacheKey = !string.IsNullOrEmpty(path) ? path : processName;
        if (_iconCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        ImageSource? icon = ExtractProcessIcon(path);
        if (icon != null)
        {
            _iconCache[cacheKey] = icon;
        }
        return icon;
    }

    private static ImageSource? ExtractProcessIcon(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        try
        {
            var shinfo = new SHFILEINFO();
            IntPtr hImg = SHGetFileInfo(path, 0, out shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
            if (hImg != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                DestroyIcon(shinfo.hIcon);
                return bitmap;
            }
        }
        catch
        {
            // Error de extracción de icono no debe interrumpir la UI
        }
        return null;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    #endregion
}

public class AudioMixerItemViewModel : INotifyPropertyChanged
{
    private readonly AudioMixerService _service;
    private float _volume;
    private bool _isMuted;
    private readonly ImageSource? _icon;
    private bool _isUpdatingFromModel;

    public AudioMixerItemViewModel(AppAudioSession session, AudioMixerService service, ImageSource? icon)
    {
        _service = service;
        ProcessId = session.ProcessId;
        ProcessName = session.ProcessName;
        DisplayName = session.DisplayName;
        _volume = session.Volume;
        _isMuted = session.IsMuted;
        ProcessPath = session.ProcessPath;
        IsSystemSounds = session.IsSystemSounds;
        _icon = icon;
    }

    public int ProcessId { get; }
    public string ProcessName { get; }
    public string DisplayName { get; }
    public string? ProcessPath { get; }
    public bool IsSystemSounds { get; }
    public ImageSource? Icon => _icon;
    public Visibility IconVisibility => _icon != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FallbackVisibility => _icon == null ? Visibility.Visible : Visibility.Collapsed;

    public double SliderValue
    {
        get => Math.Round(_volume * 100);
        set
        {
            if (_isUpdatingFromModel) return;

            float newVol = (float)Math.Clamp(value / 100.0, 0.0, 1.0);
            if (Math.Abs(_volume - newVol) > 0.005f)
            {
                _volume = newVol;
                _service.SetVolume(ProcessId, _volume);
                OnPropertyChanged(nameof(SliderValue));
                OnPropertyChanged(nameof(VolumePercentText));

                // Si se sube volumen estando silenciado, reactivar audio automáticamente
                if (_volume > 0.01f && _isMuted)
                {
                    _isMuted = false;
                    _service.SetMute(ProcessId, false);
                    OnPropertyChanged(nameof(IsMuted));
                    OnPropertyChanged(nameof(MuteIcon));
                }
            }
        }
    }

    public string VolumePercentText => $"{Math.Round(_volume * 100)}%";

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted != value)
            {
                _isMuted = value;
                _service.SetMute(ProcessId, _isMuted);
                OnPropertyChanged(nameof(IsMuted));
                OnPropertyChanged(nameof(MuteIcon));
            }
        }
    }

    public string MuteIcon => _isMuted ? "🔇" : "🔊";

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    public void UpdateFromSession(AppAudioSession session)
    {
        _isUpdatingFromModel = true;
        try
        {
            if (Math.Abs(_volume - session.Volume) > 0.015f)
            {
                _volume = session.Volume;
                OnPropertyChanged(nameof(SliderValue));
                OnPropertyChanged(nameof(VolumePercentText));
            }

            if (_isMuted != session.IsMuted)
            {
                _isMuted = session.IsMuted;
                OnPropertyChanged(nameof(IsMuted));
                OnPropertyChanged(nameof(MuteIcon));
            }
        }
        finally
        {
            _isUpdatingFromModel = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
