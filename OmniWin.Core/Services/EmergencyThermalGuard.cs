using System;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class EmergencyThermalGuard
{
    public static EmergencyThermalGuard Instance { get; } = new();

    private const string POWER_SAVER_GUID = "a1841308-3541-4fab-bc81-f71556f20b4a";
    private readonly PowerService _powerService = new();
    private string? _originalPowerSchemeGuid = null;

    public bool IsEmergencyMitigationActive { get; private set; } = false;
    public bool AutoPowerMitigationEnabled { get; set; } = false;
    public int ConsecutiveCriticalSeconds { get; private set; } = 0;

    public event Action<string>? OnEmergencyTriggered;
    public event Action<string>? OnEmergencyResolved;

    public void CancelEmergencyMitigation()
    {
        IsEmergencyMitigationActive = false;
        ConsecutiveCriticalSeconds = 0;
    }

    public async Task EvaluateTemperatureTicksAsync(double? cpuTemp, double? gpuTemp, double cpuCriticalThreshold = 92.0, double gpuCriticalThreshold = 86.0)
    {
        // If neither auto emergency cooling nor power mitigation is enabled by the user, do nothing
        if (!ThermalSensorService.Instance.Settings.EnableEmergencyCooling && !AutoPowerMitigationEnabled)
        {
            ConsecutiveCriticalSeconds = 0;
            return;
        }

        // Sanity check: Real CPU/GPU temps rarely exceed 115C without system thermal trip
        if (cpuTemp.HasValue && (cpuTemp.Value <= 0 || cpuTemp.Value > 115.0)) cpuTemp = null;
        if (gpuTemp.HasValue && (gpuTemp.Value <= 0 || gpuTemp.Value > 115.0)) gpuTemp = null;

        if (!cpuTemp.HasValue && !gpuTemp.HasValue) return;

        double cTemp = cpuTemp ?? 40.0;
        double gTemp = gpuTemp ?? 40.0;

        bool isCritical = (cpuTemp.HasValue && cTemp >= cpuCriticalThreshold) || 
                          (gpuTemp.HasValue && gTemp >= gpuCriticalThreshold);

        if (isCritical)
        {
            ConsecutiveCriticalSeconds++;

            // If critical condition persists for > 5 seconds, trigger emergency protocols
            if (ConsecutiveCriticalSeconds >= 5 && !IsEmergencyMitigationActive)
            {
                await TriggerEmergencyProtocolsAsync(cTemp, gTemp);
            }
        }
        else
        {
            ConsecutiveCriticalSeconds = Math.Max(0, ConsecutiveCriticalSeconds - 1);

            // If temperature has cooled down below 75°C, restore normal state
            if (IsEmergencyMitigationActive && cTemp < 75.0 && gTemp < 75.0)
            {
                await RestoreNormalProtocolsAsync();
            }
        }
    }

    private async Task TriggerEmergencyProtocolsAsync(double cpuTemp, double gpuTemp)
    {
        IsEmergencyMitigationActive = true;

        // 1. Force all controllable fans to 100% (only if user explicitly enabled emergency cooling)
        if (ThermalSensorService.Instance.Settings.EnableEmergencyCooling)
        {
            ThermalSensorService.Instance.SetAllFansPercent(100.0f);
        }

        // 2. Temporarily switch Windows Power Scheme to Power Saver to drop clocks and heat
        if (AutoPowerMitigationEnabled)
        {
            try
            {
                var schemes = await _powerService.GetPowerSchemesAsync();
                var active = schemes.Find(s => s.IsActive);
                if (active != null)
                {
                    _originalPowerSchemeGuid = active.Guid;
                }

                await _powerService.SetActiveSchemeAsync(POWER_SAVER_GUID);
            }
            catch { }
        }

        string msg = $"🚨 Mitigación Térmica de Emergencia activada: CPU={cpuTemp:F0}°C, GPU={gpuTemp:F0}°C. Ventiladores al 100% y ahorro de energía temporal activado.";
        OnEmergencyTriggered?.Invoke(msg);
    }

    private async Task RestoreNormalProtocolsAsync()
    {
        IsEmergencyMitigationActive = false;

        // 1. Restore Fans to automatic / current curve
        ThermalSensorService.Instance.RestoreAllFansAuto();

        // 2. Restore original Power Scheme
        if (AutoPowerMitigationEnabled && !string.IsNullOrWhiteSpace(_originalPowerSchemeGuid))
        {
            try
            {
                await _powerService.SetActiveSchemeAsync(_originalPowerSchemeGuid);
            }
            catch { }
        }

        string msg = "✔ Temperatura normalizada (<75°C). Mitigación de emergencia desactivada y plan de energía restaurado.";
        OnEmergencyResolved?.Invoke(msg);
    }
}
