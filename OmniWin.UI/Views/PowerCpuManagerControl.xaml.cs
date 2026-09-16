using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class PowerCpuManagerControl : UserControl
{
    private readonly PowerService _powerService = new();
    private readonly CpuOptimizationService _cpuService = CpuOptimizationService.Instance;
    private readonly UniversalGpuService _gpuService = UniversalGpuService.Instance;
    private readonly BatteryHealthService _batteryService = new();

    private List<PowerSchemeInfo> _schemes = new();
    private CpuDetails? _cpuDetails;
    private string _activeSchemeGuid = string.Empty;
    private string _activeSchemeName = string.Empty;
    private bool _isExtremeSaverActive = false;

    public PowerCpuManagerControl()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async void BtnRefreshAll_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    public async Task RefreshAllAsync()
    {
        try
        {
            // 1. Cargar detalles de CPU
            _cpuDetails = await Task.Run(() => _cpuService.GetCpuDetails());
            TxtCpuHeaderName.Text = _cpuDetails.Name;
            TxtCpuArchChip.Text = _cpuDetails.ArchitectureType switch
            {
                CpuArchitectureType.IntelHybrid_P_E_Cores => "Intel Híbrido (P-Cores + E-Cores)",
                CpuArchitectureType.AmdRyzen_3D_VCache => "AMD Ryzen (3D V-Cache)",
                CpuArchitectureType.AmdRyzen_Standard => "AMD Ryzen Multi-Core",
                _ => $"{_cpuDetails.Vendor} Arquitectura Estándar"
            };
            TxtCpuThreadsChip.Text = $"{_cpuDetails.LogicalProcessors} Hilos Lógicos";

            // Sincronizar sliders de silicio
            SliderCoreParking.Value = _cpuDetails.CoreParkingMinPercent;
            TxtCoreParkingVal.Text = $"{_cpuDetails.CoreParkingMinPercent}%";

            SliderEpp.Value = _cpuDetails.EnergyPerformancePreference;
            TxtEppVal.Text = $"{_cpuDetails.EnergyPerformancePreference}%";

            // 2. Timer resolution
            var timer = _powerService.GetTimerResolution();
            TxtTimerResChip.Text = $"{timer.currentMs:F1} ms";

            // 3. Cargar Esquemas de Energía
            _schemes = await _powerService.GetPowerSchemesAsync();
            IcPowerSchemes.ItemsSource = _schemes;

            var active = _schemes.FirstOrDefault(s => s.IsActive);
            if (active != null)
            {
                _activeSchemeGuid = active.Guid;
                _activeSchemeName = active.Name;
                TxtActivePlanChip.Text = active.Name;
            }
            else
            {
                TxtActivePlanChip.Text = "Esquema Personalizado";
            }

            // 4. Recomendaciones para el plan activo
            UpdateRecommendationsUi(_activeSchemeName);

            // 5. Cargar GPUs Universales (NVIDIA, AMD Radeon SAM, Intel)
            var gpus = await Task.Run(() => _gpuService.GetInstalledGpus());
            IcGpus.ItemsSource = gpus;

            // 6. Cargar Diagnóstico de Batería
            RefreshBatteryReport();
        }
        catch (Exception ex)
        {
            TxtApplyFeedback.Text = $"Error al cargar telemetría: {ex.Message}";
        }
    }

    private void UpdateRecommendationsUi(string schemeName)
    {
        var rec = _powerService.GetProfileRecommendation(schemeName);
        TxtRecTargetTitle.Text = $"Perfil Objetivo: {rec.TargetProfileName}";
        TxtRecCoreParking.Text = rec.CpuCoreParkingRecommendation;
        TxtRecEpp.Text = rec.EnergyPreferenceRecommendation;
        TxtRecHetero.Text = rec.HeterogeneousRecommendation;
        TxtRecHighlights.Text = string.Join(Environment.NewLine, rec.Highlights.Select(h => $"• {h}"));
    }

    private async void BtnActivateScheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string guid && !string.IsNullOrWhiteSpace(guid))
        {
            try
            {
                bool ok = await _powerService.SetActiveSchemeAsync(guid);
                if (ok)
                {
                    TxtApplyFeedback.Text = "✔ Plan de energía activado con éxito.";
                    await RefreshAllAsync();
                }
                else
                {
                    TxtApplyFeedback.Text = "No se pudo activar el plan de energía.";
                }
            }
            catch (Exception ex)
            {
                TxtApplyFeedback.Text = $"Error: {ex.Message}";
            }
        }
    }

    private async void BtnUnlockUltimate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string result = await _powerService.UnlockUltimatePerformanceSchemeAsync();
            TxtApplyFeedback.Text = "✔ Plan Ultimate Performance duplicado en tu sistema.";
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            TxtApplyFeedback.Text = $"Error al desbloquear: {ex.Message}";
        }
    }

    private async void BtnApplyRecommendations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnApplyRecommendations.IsEnabled = false;
            BtnApplyRecommendations.Content = "⏳ Aplicando...";

            bool ok = await _powerService.ApplyRecommendedTuningForSchemeAsync(_activeSchemeName);
            if (ok)
            {
                TxtApplyFeedback.Text = "✔ Ajustes de procesador (Core Parking, EPP y Scheduler) aplicados correctamente.";
                await RefreshAllAsync();
            }
            else
            {
                TxtApplyFeedback.Text = "Ajustes aplicados con advertencia del kernel.";
            }
        }
        catch (Exception ex)
        {
            TxtApplyFeedback.Text = $"Error: {ex.Message}";
        }
        finally
        {
            BtnApplyRecommendations.IsEnabled = true;
            BtnApplyRecommendations.Content = "🚀 Aplicar Ajustes Óptimos de este Perfil";
        }
    }

    private void BtnToggleTimer05_Click(object sender, RoutedEventArgs e)
    {
        var timer = _powerService.GetTimerResolution();
        bool set05 = timer.currentMs > 0.6;
        _powerService.SetHighPrecisionTimer(set05);
        var updated = _powerService.GetTimerResolution();
        TxtTimerResChip.Text = $"{updated.currentMs:F1} ms";
        BtnToggleTimer05.Content = updated.currentMs <= 0.6 ? "✔ Timer en 0.5 ms" : "⚡ Fijar a 0.5 ms";
    }

    private void SliderCoreParking_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtCoreParkingVal == null) return;
        int val = (int)e.NewValue;
        TxtCoreParkingVal.Text = $"{val}%";
        _cpuService.SetCoreParkingMinPercent(val);
    }

    private void SliderEpp_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtEppVal == null) return;
        int val = (int)e.NewValue;
        TxtEppVal.Text = $"{val}%";
        _cpuService.SetEnergyPerformancePreference(val);
    }

    private async void BtnCpuGamingPreset_Click(object sender, RoutedEventArgs e)
    {
        await Task.Run(() => _cpuService.ApplyGamingCpuTuning());
        TxtApplyFeedback.Text = "✔ Perfil Gaming CPU activo (Core Parking 100% y EPP 0%).";
        await RefreshAllAsync();
    }

    private async void BtnCpuBalancedPreset_Click(object sender, RoutedEventArgs e)
    {
        await Task.Run(() => _cpuService.ApplyBalancedCpuTuning());
        TxtApplyFeedback.Text = "✔ Perfil Equilibrado CPU activo (Core Parking 50% y EPP 50%).";
        await RefreshAllAsync();
    }

    private async void BtnCpuEcoPreset_Click(object sender, RoutedEventArgs e)
    {
        await Task.Run(() => _cpuService.ApplyEcoSilentCpuTuning());
        TxtApplyFeedback.Text = "✔ Perfil Silencio CPU activo (Tope 99% y EPP 80%).";
        await RefreshAllAsync();
    }

    private void RefreshBatteryReport()
    {
        try
        {
            var rep = _batteryService.GetBatteryReport();
            if (!rep.HasBattery)
            {
                TxtBatteryHealthBadge.Text = "CORRIENTE AC";
                TxtBatteryChargeLevel.Text = "Alimentación Fija";
                TxtBatteryEstimatedTime.Text = "Sin batería (PC Escritorio)";
                TxtBatteryHealthPercent.Text = "N/A";
                TxtBatteryWearPercent.Text = "Dispositivo estático";
                TxtBatteryCapacityReal.Text = "Red Eléctrica";
                TxtBatteryCycles.Text = "Ciclos: 0";
                TxtBatteryDischargeWatts.Text = "0.00 W";
                TxtBatteryDeviceModel.Text = "Fuente de Alimentación";
                return;
            }

            TxtBatteryHealthBadge.Text = $"{rep.HealthPercent:N0}% SALUD";
            TxtBatteryChargeLevel.Text = $"{rep.ChargePercent}% • {(rep.IsPluggedIn ? "Conectado" : "Batería")}";
            TxtBatteryEstimatedTime.Text = rep.EstimatedTimeRemaining.HasValue
                ? $"{rep.EstimatedTimeRemaining.Value.Hours}h {rep.EstimatedTimeRemaining.Value.Minutes}m restantes"
                : (rep.IsCharging ? "Cargando..." : "Calculando tiempo...");

            TxtBatteryHealthPercent.Text = $"{rep.HealthPercent:N1}% Salud";
            TxtBatteryWearPercent.Text = $"Desgaste: {rep.WearLevelPercent:N1}%";
            TxtBatteryCapacityReal.Text = $"{rep.FullChargeCapacityMWh:N0} / {rep.DesignCapacityMWh:N0} mWh";
            TxtBatteryCycles.Text = rep.CycleCount > 0 ? $"Ciclos: {rep.CycleCount}" : "Ciclos: N/D";
            TxtBatteryDischargeWatts.Text = $"{rep.CurrentDischargeRateWatts:N2} W";
            TxtBatteryDeviceModel.Text = $"{rep.DeviceName} ({rep.Chemistry})";
        }
        catch { }
    }

    private void BtnToggleExtremeSaver_Click(object sender, RoutedEventArgs e)
    {
        _isExtremeSaverActive = !_isExtremeSaverActive;
        bool ok = _batteryService.ApplyExtremeBatterySaver(_isExtremeSaverActive);
        if (ok)
        {
            BtnToggleExtremeSaver.Content = _isExtremeSaverActive ? "⚡ Restaurar Normal" : "🍃 Ahorro Extremo";
            BtnToggleExtremeSaver.Background = _isExtremeSaverActive ? new SolidColorBrush(Color.FromRgb(217, 119, 6)) : new SolidColorBrush(Color.FromRgb(5, 150, 105));
            TxtApplyFeedback.Text = _isExtremeSaverActive ? "✔ Modo Ahorro Extremo activado." : "✔ Modo de energía normal restablecido.";
        }
    }
}
