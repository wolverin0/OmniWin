using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class GamingOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9001;     // Ctrl + Shift + O (Toggle HUD)
    private const int HOTKEY_LOCK = 9002;   // Ctrl + Shift + L (Toggle Lock / Click-Through)
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_O = 0x4F;
    private const uint VK_L = 0x4C;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HardwareService _hardwareService = new();
    private readonly RtssService _rtssService = new();
    private readonly DispatcherTimer _telemetryTimer = new();
    private readonly Stopwatch _sessionStopwatch = new();
    private bool _isClickThrough = false;
    private bool _isUpdatingUiFromSettings = false;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;

    public bool SyncWithRtss { get; set; } = true;

    public GamingOverlayWindow()
    {
        InitializeComponent();

        var s = AppSettingsService.Instance.Settings;

        // Restore Position or Default to Top-Right of Primary Monitor
        if (s.HudPositionX.HasValue && s.HudPositionY.HasValue)
        {
            double maxLeft = Math.Max(0, SystemParameters.WorkArea.Width - 120);
            double maxTop = Math.Max(0, SystemParameters.WorkArea.Height - 60);
            Left = Math.Clamp(s.HudPositionX.Value, 0, maxLeft);
            Top = Math.Clamp(s.HudPositionY.Value, 0, maxTop);
        }
        else
        {
            Left = Math.Max(24, SystemParameters.WorkArea.Width - 260);
            Top = 24;
        }

        // Initialize UI values from settings
        _isUpdatingUiFromSettings = true;
        ChkCpu.IsChecked = s.HudShowCpu;
        ChkCpuTemp.IsChecked = s.HudShowCpuTemp;
        ChkGpu.IsChecked = s.HudShowGpu;
        ChkGpuTemp.IsChecked = s.HudShowGpuTemp;
        ChkRam.IsChecked = s.HudShowRam;
        ChkPing.IsChecked = s.HudShowPing;
        ChkTimer.IsChecked = s.HudShowSessionTimer;
        SliderBgOpacity.Value = s.HudBackgroundOpacity;
        SliderScale.Value = Math.Clamp(s.HudScale, 0.8, 1.6);
        _isUpdatingUiFromSettings = false;

        ApplyHudConfiguration();

        _sessionStopwatch.Start();

        _telemetryTimer.Interval = TimeSpan.FromSeconds(1.0);
        _telemetryTimer.Tick += TelemetryTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(HwndHook);

        // Register Global Hotkeys: Ctrl + Shift + O (Visibility), Ctrl + Shift + L (Lock/Unlock)
        RegisterHotKey(_hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_O);
        RegisterHotKey(_hwnd, HOTKEY_LOCK, MOD_CONTROL | MOD_SHIFT, VK_L);

        // Apply toolwindow style so it doesn't clutter Alt+Tab
        ApplyWindowStyles();

        // Subscribe to remote companion commands from mobile/tablet PWA
        CompanionServerService.Instance.OnHudRemoteActionReceived += OnCompanionHudActionReceived;

        _telemetryTimer.Start();
        UpdateMetrics();
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _telemetryTimer.Stop();
        _sessionStopwatch.Stop();

        CompanionServerService.Instance.OnHudRemoteActionReceived -= OnCompanionHudActionReceived;

        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            UnregisterHotKey(_hwnd, HOTKEY_LOCK);
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(HwndHook);
            _hwndSource = null;
        }

        _hardwareService.Dispose();
        _rtssService.Dispose();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            if (hotkeyId == HOTKEY_ID)
            {
                ToggleOverlayVisibility();
                handled = true;
            }
            else if (hotkeyId == HOTKEY_LOCK)
            {
                SetClickThrough(!_isClickThrough);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void ToggleOverlayVisibility()
    {
        if (Visibility == Visibility.Visible)
        {
            Visibility = Visibility.Collapsed;
        }
        else
        {
            Visibility = Visibility.Visible;
            Topmost = true;
        }
    }

    public void SetClickThrough(bool enable)
    {
        _isClickThrough = enable;
        ApplyWindowStyles();

        if (_isClickThrough)
        {
            BadgeMode.Background = new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B));
            TxtModeBadge.Text = "BLOQUEADO (EN JUEGO)";
            TxtModeBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
            BtnToggleLock.Content = "🔓 Desbloquear";
            BtnToggleLock.Background = new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B));
            BtnToggleLock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        }
        else
        {
            BadgeMode.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            TxtModeBadge.Text = "CONFIGURACIÓN";
            TxtModeBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            BtnToggleLock.Content = "🔒 Bloquear";
            BtnToggleLock.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            BtnToggleLock.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
        }

        ApplyBackgroundAndBorder();
    }

    private void ApplyWindowStyles()
    {
        if (_hwnd == IntPtr.Zero) return;

        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOOLWINDOW; // Hide from Alt+Tab

        if (_isClickThrough)
        {
            exStyle |= (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        }
        else
        {
            exStyle &= ~WS_EX_TRANSPARENT;
            exStyle &= ~WS_EX_NOACTIVATE;
        }

        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
    }

    private void SavePosition()
    {
        AppSettingsService.Instance.SaveSettings(s =>
        {
            s.HudPositionX = Left;
            s.HudPositionY = Top;
        });
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isClickThrough && e.ChangedButton == MouseButton.Left)
        {
            DragMove();
            SavePosition();
        }
    }

    private void BtnToggleLock_Click(object sender, RoutedEventArgs e)
    {
        SetClickThrough(!_isClickThrough);
    }

    private void BtnCloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
    }

    private void BtnToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        DrawerSettings.Visibility = DrawerSettings.Visibility == Visibility.Visible 
            ? Visibility.Collapsed 
            : Visibility.Visible;
    }

    private void BtnStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int styleIndex))
        {
            AppSettingsService.Instance.SaveSettings(s => s.HudStyleIndex = styleIndex);
            ApplyHudConfiguration();
        }
    }

    private void SliderBgOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUiFromSettings) return;

        double opacity = Math.Clamp(e.NewValue, 0.0, 1.0);
        TxtOpacityVal.Text = opacity <= 0.02 ? "0% (Puro)" : $"{(int)(opacity * 100)}%";

        AppSettingsService.Instance.SaveSettings(s => s.HudBackgroundOpacity = opacity);
        ApplyBackgroundAndBorder();
    }

    private void SliderScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUiFromSettings) return;

        double scale = Math.Clamp(e.NewValue, 0.8, 1.6);
        TxtScaleVal.Text = $"{(int)(scale * 100)}%";

        AppSettingsService.Instance.SaveSettings(s => s.HudScale = scale);
        RootBorder.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void ChkMetric_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUiFromSettings) return;

        AppSettingsService.Instance.SaveSettings(s =>
        {
            s.HudShowCpu = ChkCpu.IsChecked == true;
            s.HudShowCpuTemp = ChkCpuTemp.IsChecked == true;
            s.HudShowGpu = ChkGpu.IsChecked == true;
            s.HudShowGpuTemp = ChkGpuTemp.IsChecked == true;
            s.HudShowRam = ChkRam.IsChecked == true;
            s.HudShowPing = ChkPing.IsChecked == true;
            s.HudShowSessionTimer = ChkTimer.IsChecked == true;
            s.HudShowClock = ChkTimer.IsChecked == true;
        });

        ApplyHudConfiguration();
    }

    public void SnapToCorner(string corner)
    {
        UpdateLayout();
        double w = ActualWidth > 0 ? ActualWidth : 220;
        double h = ActualHeight > 0 ? ActualHeight : 150;
        double margin = 24;

        switch (corner.ToUpperInvariant())
        {
            case "TL":
                Left = margin;
                Top = margin;
                break;
            case "TR":
                Left = Math.Max(margin, SystemParameters.WorkArea.Width - w - margin);
                Top = margin;
                break;
            case "BL":
                Left = margin;
                Top = Math.Max(margin, SystemParameters.WorkArea.Height - h - margin);
                break;
            case "BR":
                Left = Math.Max(margin, SystemParameters.WorkArea.Width - w - margin);
                Top = Math.Max(margin, SystemParameters.WorkArea.Height - h - margin);
                break;
        }

        SavePosition();
    }

    private void BtnCorner_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string corner)
        {
            SnapToCorner(corner);
        }
    }

    private void OnCompanionHudActionReceived(string action, System.Text.Json.JsonElement payload)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                switch (action)
                {
                    case "set_hud_style":
                        if (payload.TryGetProperty("style", out var stEl) && stEl.TryGetInt32(out int style))
                        {
                            AppSettingsService.Instance.SaveSettings(s => s.HudStyleIndex = style);
                            ApplyHudConfiguration();
                        }
                        break;

                    case "set_hud_opacity":
                        if (payload.TryGetProperty("opacity", out var opEl) && opEl.TryGetDouble(out double opacity))
                        {
                            double clampedOp = Math.Clamp(opacity, 0.0, 1.0);
                            SliderBgOpacity.Value = clampedOp;
                            AppSettingsService.Instance.SaveSettings(s => s.HudBackgroundOpacity = clampedOp);
                            ApplyBackgroundAndBorder();
                        }
                        break;

                    case "set_hud_scale":
                        if (payload.TryGetProperty("scale", out var scEl) && scEl.TryGetDouble(out double scale))
                        {
                            double clampedSc = Math.Clamp(scale, 0.8, 1.6);
                            SliderScale.Value = clampedSc;
                            AppSettingsService.Instance.SaveSettings(s => s.HudScale = clampedSc);
                            RootBorder.LayoutTransform = new ScaleTransform(clampedSc, clampedSc);
                        }
                        break;

                    case "set_hud_corner":
                        if (payload.TryGetProperty("corner", out var coEl))
                        {
                            SnapToCorner(coEl.GetString() ?? "TR");
                        }
                        break;

                    case "toggle_hud_lock":
                        SetClickThrough(!_isClickThrough);
                        break;

                    case "toggle_hud_visibility":
                        ToggleOverlayVisibility();
                        break;
                }
            }
            catch { }
        });
    }

    public void ApplyHudConfiguration()
    {
        var s = AppSettingsService.Instance.Settings;
        int style = s.HudStyleIndex;

        // 1. Switch Active Panel
        PanelRivaTuner.Visibility = style == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelGlassmorphicCard.Visibility = style == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelCompactBar.Visibility = style == 2 ? Visibility.Visible : Visibility.Collapsed;

        // 2. Style selector buttons visual feedback
        BtnStyleRiva.Background = new SolidColorBrush(style == 0 ? Color.FromRgb(0x1E, 0x29, 0x3B) : Color.FromRgb(0x09, 0x0D, 0x18));
        BtnStyleRiva.Foreground = new SolidColorBrush(style == 0 ? Color.FromRgb(0x38, 0xBD, 0xF8) : Color.FromRgb(0x94, 0xA3, 0xB8));

        BtnStyleCard.Background = new SolidColorBrush(style == 1 ? Color.FromRgb(0x1E, 0x29, 0x3B) : Color.FromRgb(0x09, 0x0D, 0x18));
        BtnStyleCard.Foreground = new SolidColorBrush(style == 1 ? Color.FromRgb(0x38, 0xBD, 0xF8) : Color.FromRgb(0x94, 0xA3, 0xB8));

        BtnStyleCompact.Background = new SolidColorBrush(style == 2 ? Color.FromRgb(0x1E, 0x29, 0x3B) : Color.FromRgb(0x09, 0x0D, 0x18));
        BtnStyleCompact.Foreground = new SolidColorBrush(style == 2 ? Color.FromRgb(0x38, 0xBD, 0xF8) : Color.FromRgb(0x94, 0xA3, 0xB8));

        // 3. Layout Scale
        double scale = Math.Clamp(s.HudScale, 0.8, 1.6);
        RootBorder.LayoutTransform = new ScaleTransform(scale, scale);
        TxtScaleVal.Text = $"{(int)(scale * 100)}%";

        // 4. Background & Border
        ApplyBackgroundAndBorder();

        // 5. Metric Rows Visibility across all 3 modes
        // Style 0 (RivaTuner)
        RtssCpuRow.Visibility = s.HudShowCpu ? Visibility.Visible : Visibility.Collapsed;
        RtssGpuRow.Visibility = s.HudShowGpu ? Visibility.Visible : Visibility.Collapsed;
        RtssRamRow.Visibility = s.HudShowRam ? Visibility.Visible : Visibility.Collapsed;
        RtssPingRow.Visibility = s.HudShowPing ? Visibility.Visible : Visibility.Collapsed;
        RtssTimerRow.Visibility = (s.HudShowSessionTimer || s.HudShowClock) ? Visibility.Visible : Visibility.Collapsed;

        // Style 1 (Glassmorphic Card)
        RowCardCpu.Visibility = s.HudShowCpu ? Visibility.Visible : Visibility.Collapsed;
        RowCardGpu.Visibility = s.HudShowGpu ? Visibility.Visible : Visibility.Collapsed;
        RowCardRam.Visibility = s.HudShowRam ? Visibility.Visible : Visibility.Collapsed;
        RowCardPing.Visibility = s.HudShowPing ? Visibility.Visible : Visibility.Collapsed;
        RowCardFooter.Visibility = (s.HudShowSessionTimer || s.HudShowClock) ? Visibility.Visible : Visibility.Collapsed;

        // Style 2 (Compact Bar)
        CompactCpuBlock.Visibility = s.HudShowCpu ? Visibility.Visible : Visibility.Collapsed;
        CompactGpuBlock.Visibility = s.HudShowGpu ? Visibility.Visible : Visibility.Collapsed;
        CompactRamBlock.Visibility = s.HudShowRam ? Visibility.Visible : Visibility.Collapsed;
        CompactPingBlock.Visibility = s.HudShowPing ? Visibility.Visible : Visibility.Collapsed;
        CompactTimeBlock.Visibility = (s.HudShowSessionTimer || s.HudShowClock) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyBackgroundAndBorder()
    {
        var s = AppSettingsService.Instance.Settings;
        int style = s.HudStyleIndex;
        double opacity = Math.Clamp(s.HudBackgroundOpacity, 0.0, 1.0);

        if (style == 0) // RivaTuner Text-Only Mode (Pure floating OSD on 3D games)
        {
            if (_isClickThrough)
            {
                // In game: Header bar is completely hidden. Only text floats over game screen.
                HeaderBar.Visibility = Visibility.Collapsed;
                DrawerSettings.Visibility = Visibility.Collapsed;

                if (opacity <= 0.05)
                {
                    RootBorder.Background = Brushes.Transparent;
                    RootBorder.BorderBrush = Brushes.Transparent;
                    RootBorder.BorderThickness = new Thickness(0);
                    RootBorder.Padding = new Thickness(0);
                    RootShadow.Opacity = 0;
                }
                else
                {
                    byte alpha = (byte)(opacity * 255);
                    RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x09, 0x0D, 0x16));
                    RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.4), 0x38, 0xBD, 0xF8));
                    RootBorder.BorderThickness = new Thickness(1);
                    RootBorder.Padding = new Thickness(6, 4, 6, 4);
                    RootShadow.Opacity = opacity * 0.4;
                }
            }
            else
            {
                // Config mode: header bar visible for dragging and toggling settings
                HeaderBar.Visibility = Visibility.Visible;
                if (opacity <= 0.05)
                {
                    RootBorder.Background = new SolidColorBrush(Color.FromArgb(0x15, 0x09, 0x0D, 0x16));
                    RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x38, 0xBD, 0xF8));
                    RootBorder.BorderThickness = new Thickness(1);
                    RootBorder.Padding = new Thickness(6, 4, 6, 4);
                    RootShadow.Opacity = 0;
                }
                else
                {
                    byte alpha = (byte)(opacity * 255);
                    RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x09, 0x0D, 0x16));
                    RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
                    RootBorder.BorderThickness = new Thickness(1);
                    RootBorder.Padding = new Thickness(12, 10, 12, 10);
                    RootShadow.Opacity = opacity * 0.5;
                }
            }
        }
        else if (style == 1) // Glassmorphic Card Mode
        {
            HeaderBar.Visibility = Visibility.Visible;
            if (opacity <= 0.05)
            {
                RootBorder.Background = Brushes.Transparent;
                RootBorder.BorderBrush = new SolidColorBrush(_isClickThrough ? Brushes.Transparent.Color : Color.FromArgb(0x60, 0x38, 0xBD, 0xF8));
                RootBorder.BorderThickness = _isClickThrough ? new Thickness(0) : new Thickness(1);
                RootBorder.Padding = new Thickness(10, 8, 10, 8);
                RootShadow.Opacity = 0;
            }
            else
            {
                byte alpha = (byte)(opacity * 255);
                RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x09, 0x0D, 0x16));
                RootBorder.BorderBrush = new SolidColorBrush(_isClickThrough ? Color.FromArgb((byte)(alpha * 0.3), 0x38, 0xBD, 0xF8) : Color.FromRgb(0x38, 0xBD, 0xF8));
                RootBorder.BorderThickness = new Thickness(1);
                RootBorder.Padding = new Thickness(12, 10, 12, 10);
                RootShadow.Opacity = opacity * 0.6;
            }
        }
        else // Compact Bar Mode
        {
            if (_isClickThrough)
            {
                HeaderBar.Visibility = Visibility.Collapsed;
                DrawerSettings.Visibility = Visibility.Collapsed;

                if (opacity <= 0.05)
                {
                    RootBorder.Background = Brushes.Transparent;
                    RootBorder.BorderBrush = Brushes.Transparent;
                    RootBorder.BorderThickness = new Thickness(0);
                    RootBorder.Padding = new Thickness(2);
                    RootShadow.Opacity = 0;
                }
                else
                {
                    byte alpha = (byte)(opacity * 255);
                    RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x09, 0x0D, 0x16));
                    RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.4), 0x38, 0xBD, 0xF8));
                    RootBorder.BorderThickness = new Thickness(1);
                    RootBorder.Padding = new Thickness(8, 4, 8, 4);
                    RootShadow.Opacity = opacity * 0.4;
                }
            }
            else
            {
                HeaderBar.Visibility = Visibility.Visible;
                if (opacity <= 0.05)
                {
                    RootBorder.Background = new SolidColorBrush(Color.FromArgb(0x15, 0x09, 0x0D, 0x16));
                    RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x38, 0xBD, 0xF8));
                    RootBorder.BorderThickness = new Thickness(1);
                    RootBorder.Padding = new Thickness(6, 4, 6, 4);
                    RootShadow.Opacity = 0;
                }
                else
                {
                    byte alpha = (byte)(opacity * 255);
                    RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x09, 0x0D, 0x16));
                    RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
                    RootBorder.BorderThickness = new Thickness(1);
                    RootBorder.Padding = new Thickness(10, 8, 10, 8);
                    RootShadow.Opacity = opacity * 0.5;
                }
            }
        }
    }

    private void TelemetryTimer_Tick(object? sender, EventArgs e)
    {
        UpdateMetrics();
    }

    private void UpdateMetrics()
    {
        var s = AppSettingsService.Instance.Settings;

        // 1. Time / Stopwatch
        string currentTime = DateTime.Now.ToString("HH:mm:ss");
        TimeSpan elapsed = _sessionStopwatch.Elapsed;
        string sessionTime = $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

        TxtClockTime.Text = currentTime;
        TxtSessionTimer.Text = sessionTime;
        RtssTimerVal.Text = s.HudShowClock ? currentTime : sessionTime;
        CompactTime.Text = DateTime.Now.ToString("HH:mm");

        // 2. Hardware Telemetry
        try
        {
            var snap = _hardwareService.GetTelemetrySnapshot();

            // CPU
            double cpuVal = snap.CpuLoadPercent ?? 0;
            PbCpu.Value = cpuVal;
            TxtCpuVal.Text = $"{cpuVal:F0}%";
            TxtCpuVal.Foreground = new SolidColorBrush(cpuVal > 85 ? Color.FromRgb(0xEF, 0x44, 0x44) : (cpuVal > 60 ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x10, 0xB9, 0x81)));
            RtssCpuVal.Text = $"{cpuVal:F0}%";
            CompactCpu.Text = $"{cpuVal:F0}%";

            if (snap.CpuTemperatureCelsius.HasValue && s.HudShowCpuTemp)
            {
                double cTemp = snap.CpuTemperatureCelsius.Value;
                TxtCpuTemp.Text = $"{cTemp:F0}°C";
                TxtCpuTemp.Foreground = new SolidColorBrush(cTemp > 85 ? Color.FromRgb(0xEF, 0x44, 0x44) : (cTemp > 72 ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x94, 0xA3, 0xB8)));
                RtssCpuTemp.Text = $"{cTemp:F0}°C";
                CompactCpuTemp.Text = $" {cTemp:F0}°C";
            }
            else
            {
                TxtCpuTemp.Text = "";
                RtssCpuTemp.Text = "";
                CompactCpuTemp.Text = "";
            }

            // RAM
            if (snap.RamLoadPercent.HasValue && snap.RamUsedGB.HasValue)
            {
                PbRam.Value = snap.RamLoadPercent.Value;
                string ramStr = $"{snap.RamUsedGB.Value:F1} GB";
                TxtRamVal.Text = ramStr;
                RtssRamVal.Text = ramStr;
                CompactRam.Text = ramStr;
            }

            // GPU
            double gpuVal = snap.GpuLoadPercent ?? 0;
            TxtGpuVal.Text = snap.GpuLoadPercent.HasValue ? $"{gpuVal:F0}%" : "Activa";
            PbGpu.Value = snap.GpuLoadPercent.HasValue ? gpuVal : 25;
            RtssGpuVal.Text = $"{gpuVal:F0}%";
            CompactGpu.Text = $"{gpuVal:F0}%";

            if (snap.GpuTemperatureCelsius.HasValue && s.HudShowGpuTemp)
            {
                double gTemp = snap.GpuTemperatureCelsius.Value;
                TxtGpuTemp.Text = $"{gTemp:F0}°C";
                TxtGpuTemp.Foreground = new SolidColorBrush(gTemp > 82 ? Color.FromRgb(0xEF, 0x44, 0x44) : (gTemp > 70 ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x94, 0xA3, 0xB8)));
                RtssGpuTemp.Text = $"{gTemp:F0}°C";
                CompactGpuTemp.Text = $" {gTemp:F0}°C";
            }
            else
            {
                TxtGpuTemp.Text = "";
                RtssGpuTemp.Text = "";
                CompactGpuTemp.Text = "";
            }

            // Thermal Alert Banner
            var thermalSnap = ThermalSensorService.Instance.GetSnapshot();
            if (thermalSnap.ActiveAlerts.Count > 0)
            {
                var topAlert = thermalSnap.ActiveAlerts.First();
                TxtThermalAlertMsg.Text = $"⚠️ {topAlert.Message}";
                BannerThermalAlert.Visibility = Visibility.Visible;
            }
            else
            {
                BannerThermalAlert.Visibility = Visibility.Collapsed;
            }

            // Ping
            Task.Run(() =>
            {
                long pingMs = MeasurePing();
                Dispatcher.Invoke(() =>
                {
                    if (pingMs >= 0)
                    {
                        string pingStr = $"{pingMs} ms";
                        TxtPingVal.Text = pingStr;
                        TxtPingVal.Foreground = new SolidColorBrush(pingMs > 80 ? Color.FromRgb(0xEF, 0x44, 0x44) : (pingMs > 45 ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x10, 0xB9, 0x81)));
                        RtssPingVal.Text = pingStr;
                        CompactPing.Text = pingStr;
                    }
                    else
                    {
                        TxtPingVal.Text = "Offline";
                        TxtPingVal.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                        RtssPingVal.Text = "--";
                        CompactPing.Text = "--";
                    }
                });

                // 3. RTSS Shared Memory synchronization
                if (SyncWithRtss && _rtssService.IsRtssRunning())
                {
                    string markup = _rtssService.FormatOsdMarkup(
                        snap.CpuLoadPercent,
                        snap.CpuName,
                        snap.RamUsedGB,
                        snap.RamTotalGB,
                        snap.RamLoadPercent,
                        snap.GpuLoadPercent,
                        snap.GpuName,
                        pingMs >= 0 ? pingMs : null
                    );
                    _rtssService.UpdateOsd(markup);
                }
            });
        }
        catch { }
    }

    private long MeasurePing()
    {
        try
        {
            using var pinger = new Ping();
            var reply = pinger.Send("1.1.1.1", 400);
            if (reply.Status == IPStatus.Success)
            {
                return reply.RoundtripTime;
            }
        }
        catch { }

        return -1;
    }
}
