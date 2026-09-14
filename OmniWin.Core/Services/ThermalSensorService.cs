using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace OmniWin.Core.Services;

public enum ThermalSeverity
{
    Normal,
    Warning,
    Critical
}

public class ThermalSensorReading
{
    public string Identifier { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HardwareName { get; set; } = string.Empty;
    public string HardwareType { get; set; } = string.Empty;
    public double ValueCelsius { get; set; }
    public double MaxCelsius { get; set; }
    public ThermalSeverity Severity { get; set; } = ThermalSeverity.Normal;
}

public class FanSensorReading : System.ComponentModel.INotifyPropertyChanged
{
    public string Identifier { get; set; } = string.Empty;
    public string ControlIdentifier { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HardwareName { get; set; } = string.Empty;
    public string HardwareType { get; set; } = string.Empty;

    private double _rpm;
    public double Rpm
    {
        get => _rpm;
        set
        {
            if (Math.Abs(_rpm - value) > 0.1)
            {
                _rpm = value;
                OnPropertyChanged(nameof(Rpm));
            }
        }
    }

    private double? _controlPercent;
    public double? ControlPercent
    {
        get => _controlPercent;
        set
        {
            if (_controlPercent != value)
            {
                _controlPercent = value;
                OnPropertyChanged(nameof(ControlPercent));
            }
        }
    }

    public bool CanControl { get; set; }
    public bool IsManual { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}

public class ThermalAlertEvent
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string SensorName { get; set; } = string.Empty;
    public string HardwareType { get; set; } = string.Empty;
    public double CurrentTemperature { get; set; }
    public double ThresholdTemperature { get; set; }
    public ThermalSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ThermalThresholdSettings
{
    public double CpuWarning { get; set; } = 80.0;
    public double CpuCritical { get; set; } = 90.0;

    public double GpuWarning { get; set; } = 75.0;
    public double GpuCritical { get; set; } = 85.0;

    public double StorageWarning { get; set; } = 60.0;
    public double StorageCritical { get; set; } = 70.0;

    public bool EnableAudioAlarm { get; set; } = false;
    public bool EnableEmergencyCooling { get; set; } = false;
    public bool EnableOverlayWarning { get; set; } = true;
}

public class ThermalSnapshot
{
    public List<ThermalSensorReading> Temperatures { get; set; } = new();
    public List<FanSensorReading> Fans { get; set; } = new();
    public List<ThermalAlertEvent> ActiveAlerts { get; set; } = new();
    public double? CpuPackageTemp { get; set; }
    public double? GpuCoreTemp { get; set; }
    public double? MaxStorageTemp { get; set; }
    public ThermalSeverity GlobalSeverity { get; set; } = ThermalSeverity.Normal;
}

/// <summary>
/// Professional Hardware Sensor & Fan Control service powered by LibreHardwareMonitor.
/// Provides deep multi-sensor temperature telemetry, fan RPM & speed control,
/// configurable thresholds and multi-tiered thermal alarms.
/// </summary>
public class ThermalSensorService : IDisposable
{
    public static ThermalSensorService Instance { get; } = new();

    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly object _lock = new();
    private readonly object _snapshotLock = new();
    private ThermalSnapshot _cachedSnapshot = new();
    private Task? _backgroundPoller;
    private readonly System.Threading.CancellationTokenSource _cts = new();
    private bool _isInitialized = false;

    public ThermalThresholdSettings Settings { get; } = new();
    public List<ThermalAlertEvent> AlertHistory { get; } = new();
    public event Action<ThermalAlertEvent>? OnThermalAlertTriggered;

    private DateTime _lastBeepTime = DateTime.MinValue;

    public ThermalSensorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = true
        };

        // Start background polling immediately so cached snapshots are always fresh
        StartBackgroundPolling(1200);
    }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return;

            try
            {
                _computer.Open();
                _computer.Accept(_visitor);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThermalSensorService] LHM open warning: {ex.Message}");
            }
        }
    }

    public void StartBackgroundPolling(int intervalMs = 1200)
    {
        lock (_lock)
        {
            if (_backgroundPoller != null) return;
            _backgroundPoller = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var snap = ComputeSnapshotInternal();
                        lock (_snapshotLock)
                        {
                            _cachedSnapshot = snap;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ThermalSensorService] Background poll error: {ex.Message}");
                    }

                    try
                    {
                        await Task.Delay(intervalMs, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, _cts.Token);
        }
    }

    public ThermalSnapshot GetSnapshot()
    {
        lock (_snapshotLock)
        {
            if (_cachedSnapshot.Temperatures.Count > 0 || _cachedSnapshot.CpuPackageTemp.HasValue || _cachedSnapshot.Fans.Count > 0)
            {
                return _cachedSnapshot;
            }
        }

        // Fast fallback if poller hasn't produced first snapshot yet
        var initial = ComputeSnapshotInternal();
        lock (_snapshotLock)
        {
            _cachedSnapshot = initial;
        }
        return initial;
    }

    private ThermalSnapshot ComputeSnapshotInternal()
    {
        var snapshot = new ThermalSnapshot();

        lock (_lock)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            try
            {
                _computer.Accept(_visitor);

                foreach (var hardware in _computer.Hardware)
                {
                    ProcessHardware(hardware, snapshot);

                    foreach (var sub in hardware.SubHardware)
                    {
                        ProcessHardware(sub, snapshot);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThermalSensorService] Snapshot error: {ex.Message}");
            }

            // Fallback: If GPU temperature was not populated by LHM, check nvidia-smi
            if (!snapshot.GpuCoreTemp.HasValue)
            {
                TryFallbackNvidiaGpu(snapshot);
            }

            // Calculate Key Metrics
            var cpuTemps = snapshot.Temperatures.Where(t => t.HardwareType.Equals("Cpu", StringComparison.OrdinalIgnoreCase)).ToList();
            if (cpuTemps.Count > 0)
            {
                var pkg = cpuTemps.FirstOrDefault(t => t.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)) ?? cpuTemps.First();
                snapshot.CpuPackageTemp = pkg.ValueCelsius;
            }

            var gpuTemps = snapshot.Temperatures.Where(t => t.HardwareType.Equals("Gpu", StringComparison.OrdinalIgnoreCase)).ToList();
            if (gpuTemps.Count > 0)
            {
                var core = gpuTemps.FirstOrDefault(t => t.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) ?? gpuTemps.First();
                snapshot.GpuCoreTemp = core.ValueCelsius;
            }

            var ssdTemps = snapshot.Temperatures.Where(t => t.HardwareType.Equals("Storage", StringComparison.OrdinalIgnoreCase)).ToList();
            if (ssdTemps.Count > 0)
            {
                snapshot.MaxStorageTemp = ssdTemps.Max(t => t.ValueCelsius);
            }

            // Evaluate Thresholds & Trigger Alarms
            EvaluateThresholds(snapshot);
        }

        return snapshot;
    }

    private void ProcessHardware(IHardware hardware, ThermalSnapshot snapshot)
    {
        string hwType = hardware.HardwareType.ToString();
        string normalizedType = hwType switch
        {
            "Cpu" => "Cpu",
            "GpuNvidia" or "GpuAmd" or "GpuIntel" => "Gpu",
            "Storage" => "Storage",
            "Motherboard" or "SuperIO" => "Motherboard",
            _ => hwType
        };

        // 1. Process Temperatures
        foreach (var sensor in hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature))
        {
            if (sensor.Value.HasValue)
            {
                double val = Math.Round(sensor.Value.Value, 1);
                // Filter out disconnected Super I/O pins (e.g. 105C, 110C, 127C) or out-of-range readings
                if (val <= 0 || val > 120.0) continue;
                if (normalizedType == "Motherboard" && val >= 90.0) continue;

                snapshot.Temperatures.Add(new ThermalSensorReading
                {
                    Identifier = sensor.Identifier.ToString(),
                    Name = sensor.Name,
                    HardwareName = hardware.Name,
                    HardwareType = normalizedType,
                    ValueCelsius = val,
                    MaxCelsius = Math.Round(sensor.Max ?? sensor.Value.Value, 1)
                });
            }
        }

        // 2. Process Fans & Controls
        var fans = hardware.Sensors.Where(s => s.SensorType == SensorType.Fan).ToList();
        var controls = hardware.Sensors.Where(s => s.SensorType == SensorType.Control).ToList();

        foreach (var fan in fans)
        {
            if (fan.Value.HasValue)
            {
                var matchingControl = controls.FirstOrDefault(c => c.Index == fan.Index)
                                   ?? controls.FirstOrDefault(c => c.Name.Contains(fan.Name, StringComparison.OrdinalIgnoreCase) || fan.Name.Contains(c.Name, StringComparison.OrdinalIgnoreCase));
                
                string ctrlId = matchingControl?.Identifier.ToString() ?? fan.Identifier.ToString().Replace("/fan/", "/control/");

                snapshot.Fans.Add(new FanSensorReading
                {
                    Identifier = fan.Identifier.ToString(),
                    ControlIdentifier = ctrlId,
                    Name = fan.Name,
                    HardwareName = hardware.Name,
                    HardwareType = normalizedType,
                    Rpm = Math.Round(fan.Value.Value, 0),
                    ControlPercent = matchingControl?.Value.HasValue == true ? Math.Round(matchingControl.Value.Value, 0) : null,
                    CanControl = matchingControl?.Control != null,
                    IsManual = matchingControl?.Control?.ControlMode == ControlMode.Software
                });
            }
        }
    }

    private void TryFallbackNvidiaGpu(ThermalSnapshot snapshot)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,temperature.gpu,fan.speed --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            proc.Start();
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);

            if (!string.IsNullOrWhiteSpace(output))
            {
                var parts = output.Split(',');
                if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), out double temp))
                {
                    snapshot.GpuCoreTemp = temp;
                    snapshot.Temperatures.Add(new ThermalSensorReading
                    {
                        Identifier = "nvidia_smi_gpu_temp",
                        Name = "GPU Core",
                        HardwareName = parts[0].Trim(),
                        HardwareType = "Gpu",
                        ValueCelsius = temp,
                        MaxCelsius = temp
                    });

                    if (parts.Length >= 3 && double.TryParse(parts[2].Trim(), out double fanPercent))
                    {
                        snapshot.Fans.Add(new FanSensorReading
                        {
                            Identifier = "nvidia_smi_gpu_fan",
                            Name = "GPU Fan",
                            HardwareName = parts[0].Trim(),
                            HardwareType = "Gpu",
                            Rpm = 0,
                            ControlPercent = fanPercent,
                            CanControl = false,
                            IsManual = false
                        });
                    }
                }
            }
        }
        catch { }
    }

    private void EvaluateThresholds(ThermalSnapshot snapshot)
    {
        ThermalSeverity highest = ThermalSeverity.Normal;

        foreach (var reading in snapshot.Temperatures)
        {
            // Only evaluate CPU, GPU, and Storage for alerts; ignore Motherboard ambient/floating sensors
            if (reading.HardwareType != "Cpu" && reading.HardwareType != "Gpu" && reading.HardwareType != "Storage")
            {
                reading.Severity = ThermalSeverity.Normal;
                continue;
            }

            double warnLimit = reading.HardwareType switch
            {
                "Cpu" => Settings.CpuWarning,
                "Gpu" => Settings.GpuWarning,
                "Storage" => Settings.StorageWarning,
                _ => 85.0
            };

            double critLimit = reading.HardwareType switch
            {
                "Cpu" => Settings.CpuCritical,
                "Gpu" => Settings.GpuCritical,
                "Storage" => Settings.StorageCritical,
                _ => 95.0
            };

            if (reading.ValueCelsius >= critLimit)
            {
                reading.Severity = ThermalSeverity.Critical;
                if (highest < ThermalSeverity.Critical) highest = ThermalSeverity.Critical;

                var alert = new ThermalAlertEvent
                {
                    SensorName = $"{reading.HardwareName} - {reading.Name}",
                    HardwareType = reading.HardwareType,
                    CurrentTemperature = reading.ValueCelsius,
                    ThresholdTemperature = critLimit,
                    Severity = ThermalSeverity.Critical,
                    Message = $"Temperatura CRÍTICA en {reading.HardwareName} ({reading.ValueCelsius:F1}°C ≥ {critLimit:F0}°C)"
                };
                snapshot.ActiveAlerts.Add(alert);
                RecordAlert(alert);
            }
            else if (reading.ValueCelsius >= warnLimit)
            {
                reading.Severity = ThermalSeverity.Warning;
                if (highest < ThermalSeverity.Warning) highest = ThermalSeverity.Warning;

                var alert = new ThermalAlertEvent
                {
                    SensorName = $"{reading.HardwareName} - {reading.Name}",
                    HardwareType = reading.HardwareType,
                    CurrentTemperature = reading.ValueCelsius,
                    ThresholdTemperature = warnLimit,
                    Severity = ThermalSeverity.Warning,
                    Message = $"Temperatura ELEVADA en {reading.HardwareName} ({reading.ValueCelsius:F1}°C ≥ {warnLimit:F0}°C)"
                };
                snapshot.ActiveAlerts.Add(alert);
                RecordAlert(alert);
            }
            else
            {
                reading.Severity = ThermalSeverity.Normal;
            }
        }

        snapshot.GlobalSeverity = highest;

        // Sound alert (only if enabled by user and rate-limited)
        if (highest == ThermalSeverity.Critical && Settings.EnableAudioAlarm)
        {
            PlayCriticalBeep();
        }
    }

    private void RecordAlert(ThermalAlertEvent alert)
    {
        if (AlertHistory.Count > 100) AlertHistory.RemoveAt(0);
        AlertHistory.Add(alert);
        OnThermalAlertTriggered?.Invoke(alert);
    }

    private void PlayCriticalBeep()
    {
        if ((DateTime.Now - _lastBeepTime).TotalSeconds >= 4)
        {
            _lastBeepTime = DateTime.Now;
            Task.Run(() =>
            {
                try
                {
                    Console.Beep(1200, 300);
                    Task.Delay(100).Wait();
                    Console.Beep(1500, 400);
                }
                catch { }
            });
        }
    }

    /// <summary>
    /// Sets fan speed for a specific sensor control to a manual percentage (0 - 100).
    /// </summary>
    public bool SetFanSpeed(string sensorIdentifier, float percent)
    {
        lock (_lock)
        {
            float clamped = Math.Clamp(percent, 0f, 100f);

            foreach (var hw in _computer.Hardware)
            {
                if (TrySetControlOnHardware(hw, sensorIdentifier, clamped))
                    return true;

                foreach (var sub in hw.SubHardware)
                {
                    if (TrySetControlOnHardware(sub, sensorIdentifier, clamped))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Restores a fan control to default hardware/BIOS automated curve.
    /// </summary>
    public bool RestoreFanAuto(string sensorIdentifier)
    {
        lock (_lock)
        {
            foreach (var hw in _computer.Hardware)
            {
                if (TryRestoreControlOnHardware(hw, sensorIdentifier))
                    return true;

                foreach (var sub in hw.SubHardware)
                {
                    if (TryRestoreControlOnHardware(sub, sensorIdentifier))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Sets all controllable fans to the specified percentage (e.g. 100% emergency ramp).
    /// </summary>
    public void SetAllFansPercent(float percent)
    {
        lock (_lock)
        {
            float clamped = Math.Clamp(percent, 0f, 100f);

            foreach (var hw in _computer.Hardware)
            {
                SetAllControlsOnHardware(hw, clamped);
                foreach (var sub in hw.SubHardware)
                {
                    SetAllControlsOnHardware(sub, clamped);
                }
            }
        }
    }

    /// <summary>
    /// Restores all fans to automatic BIOS/hardware curve control.
    /// </summary>
    public void RestoreAllFansAuto()
    {
        lock (_lock)
        {
            foreach (var hw in _computer.Hardware)
            {
                RestoreAllControlsOnHardware(hw);
                foreach (var sub in hw.SubHardware)
                {
                    RestoreAllControlsOnHardware(sub);
                }
            }
        }

        try
        {
            EmergencyThermalGuard.Instance.CancelEmergencyMitigation();
        }
        catch { }
    }

    private bool TrySetControlOnHardware(IHardware hw, string identifier, float percent)
    {
        string targetControlId = identifier.Replace("/fan/", "/control/");

        foreach (var sensor in hw.Sensors.Where(s => s.SensorType == SensorType.Control))
        {
            string sId = sensor.Identifier.ToString();
            if (sId.Equals(identifier, StringComparison.OrdinalIgnoreCase) ||
                sId.Equals(targetControlId, StringComparison.OrdinalIgnoreCase) ||
                sensor.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase) ||
                (identifier.Contains("gpu", StringComparison.OrdinalIgnoreCase) && hw.HardwareType.ToString().Contains("Gpu")))
            {
                sensor.Control?.SetSoftware(percent);
                return true;
            }
        }
        return false;
    }

    private bool TryRestoreControlOnHardware(IHardware hw, string identifier)
    {
        string targetControlId = identifier.Replace("/fan/", "/control/");

        foreach (var sensor in hw.Sensors.Where(s => s.SensorType == SensorType.Control))
        {
            string sId = sensor.Identifier.ToString();
            if (sId.Equals(identifier, StringComparison.OrdinalIgnoreCase) ||
                sId.Equals(targetControlId, StringComparison.OrdinalIgnoreCase) ||
                sensor.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase) ||
                (identifier.Contains("gpu", StringComparison.OrdinalIgnoreCase) && hw.HardwareType.ToString().Contains("Gpu")))
            {
                sensor.Control?.SetDefault();
                return true;
            }
        }
        return false;
    }

    private void SetAllControlsOnHardware(IHardware hw, float percent)
    {
        foreach (var sensor in hw.Sensors.Where(s => s.SensorType == SensorType.Control))
        {
            sensor.Control?.SetSoftware(percent);
        }
    }

    private void RestoreAllControlsOnHardware(IHardware hw)
    {
        foreach (var sensor in hw.Sensors.Where(s => s.SensorType == SensorType.Control))
        {
            sensor.Control?.SetDefault();
        }
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            _cts.Dispose();
        }
        catch { }

        lock (_lock)
        {
            if (_isInitialized)
            {
                try
                {
                    RestoreAllFansAuto();
                    _computer.Close();
                }
                catch { }
                _isInitialized = false;
            }
        }
        GC.SuppressFinalize(this);
    }

    private class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware)
                subHardware.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
