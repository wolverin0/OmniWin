using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class ThermalCoolerControl : UserControl
{
    private readonly DispatcherTimer _timer = new();
    private readonly ThermalSensorService _thermalService = ThermalSensorService.Instance;
    private readonly FanCurveService _fanCurveService = FanCurveService.Instance;
    private readonly DynamicThermalProfileService _profileService = DynamicThermalProfileService.Instance;
    private readonly EmergencyThermalGuard _emergencyGuard = EmergencyThermalGuard.Instance;
    private readonly ThermalHealthDiagnosticsService _healthService = ThermalHealthDiagnosticsService.Instance;
    private readonly System.Collections.ObjectModel.ObservableCollection<FanSensorReading> _fansCollection = new();
    private int _tickCounter = 0;

    public ThermalCoolerControl()
    {
        InitializeComponent();

        IcFans.ItemsSource = _fansCollection;

        _timer.Interval = TimeSpan.FromSeconds(1.0);
        _timer.Tick += (s, e) => RefreshTelemetry();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        // Load initial threshold values into sliders
        SliderCpuWarn.Value = _thermalService.Settings.CpuWarning;
        SliderCpuCrit.Value = _thermalService.Settings.CpuCritical;
        SliderGpuWarn.Value = _thermalService.Settings.GpuWarning;
        SliderGpuCrit.Value = _thermalService.Settings.GpuCritical;
        SliderStorageWarn.Value = _thermalService.Settings.StorageWarning;
        SliderStorageCrit.Value = _thermalService.Settings.StorageCritical;

        ChkAudioAlarm.IsChecked = _thermalService.Settings.EnableAudioAlarm;
        ChkOverlayBanner.IsChecked = _thermalService.Settings.EnableOverlayWarning;
        ChkEmergencyCooling.IsChecked = _thermalService.Settings.EnableEmergencyCooling;
        ChkAutoGameProfile.IsChecked = _profileService.AutoSwitchEnabled;
        ChkEmergencyPowerMitigate.IsChecked = _emergencyGuard.AutoPowerMitigationEnabled;

        CmbFanProfiles.ItemsSource = _fanCurveService.AvailableProfiles.Select(p => p.Name).ToList();
        CmbFanProfiles.SelectedItem = _fanCurveService.ActiveProfile.Name;
        TxtCurveDescription.Text = _fanCurveService.ActiveProfile.Description;

        RefreshTelemetry();
        _timer.Start();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
    }

    private void RefreshTelemetry()
    {
        try
        {
            _tickCounter++;
            var snap = _thermalService.GetSnapshot();

            // 1. CPU
            if (snap.CpuPackageTemp.HasValue)
            {
                double cpu = snap.CpuPackageTemp.Value;
                PbCpuTemp.Value = cpu;
                TxtCpuTempBig.Text = $"{cpu:F0}°C";
                ApplyTempStyling(TxtCpuTempBig, PbCpuTemp, TxtCpuTripStatus, cpu, _thermalService.Settings.CpuWarning, _thermalService.Settings.CpuCritical);
            }

            // 2. GPU
            if (snap.GpuCoreTemp.HasValue)
            {
                double gpu = snap.GpuCoreTemp.Value;
                PbGpuTemp.Value = gpu;
                TxtGpuTempBig.Text = $"{gpu:F0}°C";
                ApplyTempStyling(TxtGpuTempBig, PbGpuTemp, TxtGpuTripStatus, gpu, _thermalService.Settings.GpuWarning, _thermalService.Settings.GpuCritical);
            }

            // 3. Storage (Primary NVMe + Disipador check)
            if (snap.PrimaryStorageTemp.HasValue || snap.MaxStorageTemp.HasValue)
            {
                double ssd = snap.PrimaryStorageTemp ?? snap.MaxStorageTemp!.Value;
                PbStorageTemp.Value = ssd;
                TxtStorageTempBig.Text = $"{ssd:F0}°C";
                if (!string.IsNullOrWhiteSpace(snap.PrimaryStorageName))
                {
                    TxtStorageModel.Text = snap.PrimaryStorageName;
                }
                bool isNvme = snap.PrimaryStorageName?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) == true ||
                              snap.PrimaryStorageName?.Contains("SSD", StringComparison.OrdinalIgnoreCase) == true ||
                              snap.PrimaryStorageName?.Contains("Samsung", StringComparison.OrdinalIgnoreCase) == true;

                double storageWarn = isNvme ? Math.Max(70.0, _thermalService.Settings.StorageWarning) : _thermalService.Settings.StorageWarning;
                double storageCrit = isNvme ? Math.Max(80.0, _thermalService.Settings.StorageCritical) : _thermalService.Settings.StorageCritical;

                if (snap.StorageHotspotTemp.HasValue && snap.StorageHotspotTemp.Value > ssd)
                {
                    TxtStorageThresholds.Text = $"NAND: {ssd:F0}°C • Hotspot ASIC: {snap.StorageHotspotTemp:F0}°C (Límite 105°C)";
                }
                else
                {
                    TxtStorageThresholds.Text = $"NAND Disipada • Umbrales: Warn {storageWarn:F0}°C • Crit {storageCrit:F0}°C";
                }
                ApplyTempStyling(TxtStorageTempBig, PbStorageTemp, TxtStorageTripStatus, ssd, storageWarn, storageCrit);
            }

            // 4. Global Badge Status
            switch (snap.GlobalSeverity)
            {
                case ThermalSeverity.Critical:
                    DotThermalStatus.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                    TxtThermalBadge.Text = "CRÍTICO";
                    TxtThermalBadge.Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5));
                    BadgeThermalState.Background = new SolidColorBrush(Color.FromRgb(0x7F, 0x1D, 0x1D));
                    break;
                case ThermalSeverity.Warning:
                    DotThermalStatus.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                    TxtThermalBadge.Text = "ADVERTENCIA";
                    TxtThermalBadge.Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0xD3, 0x4D));
                    BadgeThermalState.Background = new SolidColorBrush(Color.FromRgb(0x78, 0x35, 0x0F));
                    break;
                default:
                    DotThermalStatus.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                    TxtThermalBadge.Text = "NORMAL";
                    TxtThermalBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
                    BadgeThermalState.Background = new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B));
                    break;
            }

            // 5. Fans List (smooth in-place mutation to eliminate garbage collection & layout relayout)
            SyncFans(snap.Fans);
            BoxNoFansFound.Visibility = _fansCollection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtFanSummaryCount.Text = $"{_fansCollection.Count} ventilador(es) detectado(s)";

            // 6. Dynamic Game Profile (every 3 seconds)
            if (_tickCounter % 3 == 0)
            {
                _profileService.CheckForegroundProcessAndApplyProfile();
                if (_profileService.IsGameDetected)
                {
                    TxtCurveDescription.Text = $"🎮 Juego detectado: '{_profileService.DetectedForegroundProcess}' -> Perfil Gamer forzado para máxima disipación.";
                    if (CmbFanProfiles.SelectedItem?.ToString() != _fanCurveService.ActiveProfile.Name)
                    {
                        CmbFanProfiles.SelectedItem = _fanCurveService.ActiveProfile.Name;
                    }
                }
            }

            // 7. Thermal Health & Paste Diagnostics (every 5 seconds)
            if (_tickCounter % 5 == 1)
            {
                var health = _healthService.EvaluateSystemHealth(snap.CpuPackageTemp, null, null, snap.GpuCoreTemp, snap.MaxStorageTemp);
                TxtHealthScore.Text = $"{health.HealthScore}%";
                TxtHealthScore.Foreground = new SolidColorBrush(health.HealthScore >= 80 ? Color.FromRgb(0x10, 0xB9, 0x81) : (health.HealthScore >= 60 ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0xEF, 0x44, 0x44)));
                TxtHealthStatus.Text = health.Status;
                TxtHealthSummary.Text = health.Summary;
                TxtThermalResistance.Text = health.EstimatedThermalResistance > 0 ? $"Resistencia: {health.EstimatedThermalResistance:F2} °C/W" : "Resistencia: ~0.26 °C/W";
            }

            // 8. Emergency Thermal Guard Ticks (only if explicitly enabled by user)
            if (_thermalService.Settings.EnableEmergencyCooling || _emergencyGuard.AutoPowerMitigationEnabled)
            {
                _ = _emergencyGuard.EvaluateTemperatureTicksAsync(snap.CpuPackageTemp, snap.GpuCoreTemp, _thermalService.Settings.CpuCritical, _thermalService.Settings.GpuCritical);
            }
        }
        catch (Exception ex)
        {
            TxtFanSummaryCount.Text = $"Error: {ex.Message}";
        }
    }

    private void SyncFans(System.Collections.Generic.List<FanSensorReading> freshFans)
    {
        if (_fansCollection.Count != freshFans.Count)
        {
            _fansCollection.Clear();
            foreach (var f in freshFans)
            {
                _fansCollection.Add(f);
            }
        }
        else
        {
            for (int i = 0; i < freshFans.Count; i++)
            {
                _fansCollection[i].Rpm = freshFans[i].Rpm;
                _fansCollection[i].ControlPercent = freshFans[i].ControlPercent;
                _fansCollection[i].IsManual = freshFans[i].IsManual;
                _fansCollection[i].CanControl = freshFans[i].CanControl;
            }
        }
    }

    private void ApplyTempStyling(TextBlock txtTemp, ProgressBar pb, TextBlock txtStatus, double temp, double warn, double crit)
    {
        if (temp >= crit)
        {
            txtTemp.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            pb.Foreground = (Brush)FindResource("TempCriticalBrush");
            txtStatus.Text = "CRÍTICO";
            txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        }
        else if (temp >= warn)
        {
            txtTemp.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            pb.Foreground = (Brush)FindResource("TempWarningBrush");
            txtStatus.Text = "ELEVADO";
            txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else
        {
            txtTemp.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            pb.Foreground = (Brush)FindResource("TempNormalBrush");
            txtStatus.Text = "NORMAL";
            txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        }
    }

    private void BtnRampFans100_Click(object sender, RoutedEventArgs e)
    {
        _thermalService.SetAllFansPercent(100f);
        RefreshTelemetry();
    }

    private void BtnRestoreAllFans_Click(object sender, RoutedEventArgs e)
    {
        _thermalService.RestoreAllFansAuto();
        RefreshTelemetry();
    }

    private void BtnFanMax_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            _thermalService.SetFanSpeed(id, 100f);
            RefreshTelemetry();
        }
    }

    private void BtnFanAuto_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            _thermalService.RestoreFanAuto(id);
            RefreshTelemetry();
        }
    }

    private void FanSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.Tag is string id && slider.IsMouseCaptureWithin)
        {
            _thermalService.SetFanSpeed(id, (float)e.NewValue);
        }
    }

    private void SliderThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;

        if (sender == SliderCpuWarn)
        {
            _thermalService.Settings.CpuWarning = SliderCpuWarn.Value;
            TxtCpuWarnLabel.Text = $"Advertencia: {SliderCpuWarn.Value:F0}°C";
            TxtCpuThresholds.Text = $"Umbrales: Warn {SliderCpuWarn.Value:F0}°C • Crit {_thermalService.Settings.CpuCritical:F0}°C";
        }
        else if (sender == SliderCpuCrit)
        {
            _thermalService.Settings.CpuCritical = SliderCpuCrit.Value;
            TxtCpuCritLabel.Text = $"Crítico / Emergencia: {SliderCpuCrit.Value:F0}°C";
            TxtCpuThresholds.Text = $"Umbrales: Warn {_thermalService.Settings.CpuWarning:F0}°C • Crit {SliderCpuCrit.Value:F0}°C";
        }
        else if (sender == SliderGpuWarn)
        {
            _thermalService.Settings.GpuWarning = SliderGpuWarn.Value;
            TxtGpuWarnLabel.Text = $"Advertencia: {SliderGpuWarn.Value:F0}°C";
            TxtGpuThresholds.Text = $"Umbrales: Warn {SliderGpuWarn.Value:F0}°C • Crit {_thermalService.Settings.GpuCritical:F0}°C";
        }
        else if (sender == SliderGpuCrit)
        {
            _thermalService.Settings.GpuCritical = SliderGpuCrit.Value;
            TxtGpuCritLabel.Text = $"Crítico / Emergencia: {SliderGpuCrit.Value:F0}°C";
            TxtGpuThresholds.Text = $"Umbrales: Warn {_thermalService.Settings.GpuWarning:F0}°C • Crit {SliderGpuCrit.Value:F0}°C";
        }
        else if (sender == SliderStorageWarn)
        {
            _thermalService.Settings.StorageWarning = SliderStorageWarn.Value;
            TxtStorageWarnLabel.Text = $"Advertencia: {SliderStorageWarn.Value:F0}°C";
            TxtStorageThresholds.Text = $"Umbrales: Warn {SliderStorageWarn.Value:F0}°C • Crit {_thermalService.Settings.StorageCritical:F0}°C";
        }
        else if (sender == SliderStorageCrit)
        {
            _thermalService.Settings.StorageCritical = SliderStorageCrit.Value;
            TxtStorageCritLabel.Text = $"Crítico / Throttling: {SliderStorageCrit.Value:F0}°C";
            TxtStorageThresholds.Text = $"Umbrales: Warn {_thermalService.Settings.StorageWarning:F0}°C • Crit {SliderStorageCrit.Value:F0}°C";
        }
    }

    private void AlarmOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _thermalService.Settings.EnableAudioAlarm = ChkAudioAlarm.IsChecked == true;
        _thermalService.Settings.EnableOverlayWarning = ChkOverlayBanner.IsChecked == true;
        _thermalService.Settings.EnableEmergencyCooling = ChkEmergencyCooling.IsChecked == true;
        _emergencyGuard.AutoPowerMitigationEnabled = ChkEmergencyPowerMitigate.IsChecked == true;
    }

    private void CmbFanProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || CmbFanProfiles.SelectedItem == null) return;
        string? selectedName = CmbFanProfiles.SelectedItem.ToString();
        var profile = _fanCurveService.AvailableProfiles.FirstOrDefault(p => p.Name == selectedName);
        if (profile != null)
        {
            _fanCurveService.SetActiveProfile(profile.Id);
            TxtCurveDescription.Text = profile.Description;
        }
    }

    private void ChkAutoGameProfile_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _profileService.AutoSwitchEnabled = ChkAutoGameProfile.IsChecked == true;
    }
}
