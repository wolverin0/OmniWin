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

    public double StorageWarning { get; set; } = 70.0;
    public double StorageCritical { get; set; } = 80.0;

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
    public double? GpuCoreLoad { get; set; }
    public double? CpuPowerWatts { get; set; }
    public double? GpuMemoryUsedMb { get; set; }
    public double? MaxStorageTemp { get; set; }
    public string? PrimaryStorageName { get; set; }
    public double? PrimaryStorageTemp { get; set; }
    public double? StorageHotspotTemp { get; set; }
    public string? StorageHotspotName { get; set; }
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
            else
            {
                TryFallbackCpuTemperature(snapshot);
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
                // Identify NVMe SSDs vs legacy mechanical HDDs (exclude legacy SATA like HD322HJ)
                var nvmeTemps = ssdTemps.Where(t => (t.HardwareName.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                                                    t.HardwareName.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                                    (t.HardwareName.Contains("Samsung", StringComparison.OrdinalIgnoreCase) && !t.HardwareName.Contains("HD322", StringComparison.OrdinalIgnoreCase))) &&
                                                    !t.HardwareName.Contains("HD322", StringComparison.OrdinalIgnoreCase)).ToList();

                var pool = nvmeTemps.Count > 0 ? nvmeTemps : ssdTemps;

                // Prioritize OS boot / primary drive (e.g. 990 PRO, 970 EVO Plus) for the main dashboard telemetry
                var primaryDrives = pool.Where(t => t.HardwareName.Contains("990", StringComparison.OrdinalIgnoreCase) ||
                                                    t.HardwareName.Contains("980", StringComparison.OrdinalIgnoreCase) ||
                                                    t.HardwareName.Contains("970", StringComparison.OrdinalIgnoreCase) ||
                                                    t.HardwareName.Contains("PRO", StringComparison.OrdinalIgnoreCase) ||
                                                    t.HardwareName.Contains("EVO", StringComparison.OrdinalIgnoreCase) ||
                                                    t.HardwareName.Contains("C:", StringComparison.OrdinalIgnoreCase)).ToList();
                var primaryPool = primaryDrives.Count > 0 ? primaryDrives : pool;

                // For Samsung & modern NVMe SSDs (like 970 EVO Plus / 990 PRO), NAND Flash is always the cooler sensor (typically 30-50°C),
                // while the ASIC memory controller (Phoenix / Elpis / Pascal) operates at 60-78°C.
                var nonHotspotSensors = primaryPool.Where(t =>
                    !t.Name.Contains("Temperature 2", StringComparison.OrdinalIgnoreCase) &&
                    !t.Name.Contains("Temperature 3", StringComparison.OrdinalIgnoreCase) &&
                    !t.Name.Contains("Sensor 2", StringComparison.OrdinalIgnoreCase) &&
                    !t.Name.Contains("Sensor 3", StringComparison.OrdinalIgnoreCase) &&
                    !t.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) &&
                    !t.Name.Contains("Controller", StringComparison.OrdinalIgnoreCase) &&
                    !t.Name.Contains("ASIC", StringComparison.OrdinalIgnoreCase)).ToList();

                var explicitFlash = nonHotspotSensors.FirstOrDefault(t =>
                    t.Name.Contains("Flash", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals("Temperature 1", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals("Sensor 1", StringComparison.OrdinalIgnoreCase));

                // Composite temperature (NAND Flash)
                var composite = explicitFlash
                    ?? (nonHotspotSensors.Count > 1
                        ? nonHotspotSensors.OrderBy(t => t.ValueCelsius).FirstOrDefault()
                        : nonHotspotSensors.FirstOrDefault())
                    ?? primaryPool.OrderBy(t => t.ValueCelsius).FirstOrDefault();

                var hotspot = primaryPool.Where(t =>
                    t.Name.Contains("Temperature 2", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains("Temperature 3", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains("Sensor 2", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains("Sensor 3", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains("ASIC", StringComparison.OrdinalIgnoreCase) ||
                    (composite != null && t.ValueCelsius > composite.ValueCelsius + 8.0))
                    .OrderByDescending(t => t.ValueCelsius).FirstOrDefault()
                    ?? pool.OrderByDescending(t => t.ValueCelsius).FirstOrDefault();

                snapshot.PrimaryStorageName = composite?.HardwareName ?? primaryPool.First().HardwareName;
                snapshot.PrimaryStorageTemp = composite?.ValueCelsius ?? hotspot?.ValueCelsius;
                snapshot.MaxStorageTemp = snapshot.PrimaryStorageTemp;

                if (hotspot != null && composite != null && hotspot.ValueCelsius > composite.ValueCelsius)
                {
                    snapshot.StorageHotspotTemp = hotspot.ValueCelsius;
                    snapshot.StorageHotspotName = $"{hotspot.HardwareName} ({hotspot.Name})";
                }
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

                // Storage sanitization:
                // Mechanical HDDs operate between 25°C and 48°C. Readings > 65°C on mechanical drives
                // indicate SMART Attribute 190 (Airflow Temp) normalized as (100 - temp), e.g. 100 - 16 = 84°C.
                bool isMechanicalHdd = hardware.Name.Contains("WDC", StringComparison.OrdinalIgnoreCase) ||
                                       hardware.Name.Contains("WD", StringComparison.OrdinalIgnoreCase) ||
                                       hardware.Name.Contains("ST", StringComparison.OrdinalIgnoreCase) ||
                                       hardware.Name.Contains("Seagate", StringComparison.OrdinalIgnoreCase) ||
                                       hardware.Name.Contains("Hitachi", StringComparison.OrdinalIgnoreCase) ||
                                       hardware.Name.Contains("HTS", StringComparison.OrdinalIgnoreCase) ||
                                       hardware.Name.Contains("HD322", StringComparison.OrdinalIgnoreCase);

                if (normalizedType == "Storage")
                {
                    if (sensor.Name.Contains("Airflow", StringComparison.OrdinalIgnoreCase))
                    {
                        if (val > 60.0 && val < 100.0) val = Math.Round(100.0 - val, 1);
                        else continue;
                    }
                    else if (isMechanicalHdd && val > 65.0)
                    {
                        if (val < 100.0) val = Math.Round(100.0 - val, 1);
                        else continue;
                    }
                }

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

        // 3. Process Power, Load and Memory
        if (normalizedType == "Cpu")
        {
            var powerSensors = hardware.Sensors.Where(s => s.SensorType == SensorType.Power).ToList();
            var pkgPower = powerSensors.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        ?? powerSensors.FirstOrDefault(s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                        ?? powerSensors.FirstOrDefault();
            if (pkgPower?.Value.HasValue == true)
            {
                snapshot.CpuPowerWatts = Math.Round(pkgPower.Value.Value, 1);
            }
        }
        else if (normalizedType == "Gpu")
        {
            var loadSensors = hardware.Sensors.Where(s => s.SensorType == SensorType.Load).ToList();
            var coreLoad = loadSensors.FirstOrDefault(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        ?? loadSensors.FirstOrDefault();
            if (coreLoad?.Value.HasValue == true)
            {
                snapshot.GpuCoreLoad = Math.Round(coreLoad.Value.Value, 1);
            }

            var memSensors = hardware.Sensors.Where(s => s.SensorType == SensorType.SmallData || s.SensorType == SensorType.Data).ToList();
            var memUsed = memSensors.FirstOrDefault(s => s.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                       ?? memSensors.FirstOrDefault(s => s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase));
            if (memUsed?.Value.HasValue == true)
            {
                snapshot.GpuMemoryUsedMb = Math.Round(memUsed.Value.Value, 0);
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
                Arguments = "--query-gpu=name,temperature.gpu,fan.speed,utilization.gpu,memory.used --format=csv,noheader,nounits",
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
                if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture, out double temp))
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

                    if (parts.Length >= 3 && double.TryParse(parts[2].Trim(), System.Globalization.CultureInfo.InvariantCulture, out double fanPercent))
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

                    if (parts.Length >= 4 && double.TryParse(parts[3].Trim(), System.Globalization.CultureInfo.InvariantCulture, out double gpuLoad))
                    {
                        snapshot.GpuCoreLoad = gpuLoad;
                    }

                    if (parts.Length >= 5 && double.TryParse(parts[4].Trim(), System.Globalization.CultureInfo.InvariantCulture, out double memUsedMb))
                    {
                        snapshot.GpuMemoryUsedMb = memUsedMb;
                    }
                }
            }
        }
        catch { }
    }

    private void TryFallbackCpuTemperature(ThermalSnapshot snapshot)
    {
        try
        {
            // 1. Check if Motherboard/SuperIO exposed a CPU sensor
            var mbCpu = snapshot.Temperatures.FirstOrDefault(t =>
                t.HardwareType.Equals("Motherboard", StringComparison.OrdinalIgnoreCase) &&
                (t.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                 t.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                 t.Name.Contains("Socket", StringComparison.OrdinalIgnoreCase)) &&
                t.ValueCelsius >= 20.0 && t.ValueCelsius <= 110.0);

            if (mbCpu != null)
            {
                snapshot.CpuPackageTemp = mbCpu.ValueCelsius;
                return;
            }

            // 2. Query Win32_PerfFormattedData_Counters_ThermalZoneInformation via WMI (non-elevated)
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Temperature, HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                double valC = 0;
                if (obj["HighPrecisionTemperature"] != null &&
                    double.TryParse(obj["HighPrecisionTemperature"].ToString(), out double highPrecision) &&
                    highPrecision > 2000)
                {
                    valC = (highPrecision / 10.0) - 273.15;
                }
                else if (obj["Temperature"] != null &&
                         double.TryParse(obj["Temperature"].ToString(), out double kelvin) &&
                         kelvin > 200)
                {
                    valC = kelvin - 273.15;
                }

                if (valC >= 15.0 && valC <= 115.0)
                {
                    double rounded = Math.Round(valC, 1);
                    snapshot.CpuPackageTemp = rounded;
                    snapshot.Temperatures.Add(new ThermalSensorReading
                    {
                        Identifier = "wmi_acpi_thermal_zone_cpu",
                        Name = "CPU Package (ACPI)",
                        HardwareName = "Procesador Intel Core",
                        HardwareType = "Cpu",
                        ValueCelsius = rounded,
                        MaxCelsius = rounded
                    });
                    return;
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

            bool isStorageHotspot = reading.HardwareType == "Storage" &&
                (reading.Name.Contains("Temperature 2", StringComparison.OrdinalIgnoreCase) ||
                 reading.Name.Contains("Temperature 3", StringComparison.OrdinalIgnoreCase) ||
                 reading.Name.Contains("Sensor 2", StringComparison.OrdinalIgnoreCase) ||
                 reading.Name.Contains("Sensor 3", StringComparison.OrdinalIgnoreCase) ||
                 reading.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase) ||
                 reading.Name.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                 reading.Name.Contains("ASIC", StringComparison.OrdinalIgnoreCase));

            bool isSamsungNvme = reading.HardwareType == "Storage" &&
                reading.HardwareName.Contains("Samsung", StringComparison.OrdinalIgnoreCase) &&
                (reading.HardwareName.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                 reading.HardwareName.Contains("970", StringComparison.OrdinalIgnoreCase) ||
                 reading.HardwareName.Contains("980", StringComparison.OrdinalIgnoreCase) ||
                 reading.HardwareName.Contains("990", StringComparison.OrdinalIgnoreCase) ||
                 reading.HardwareName.Contains("EVO", StringComparison.OrdinalIgnoreCase) ||
                 reading.HardwareName.Contains("PRO", StringComparison.OrdinalIgnoreCase));

            bool isNvmeDrive = reading.HardwareType == "Storage" &&
                (isSamsungNvme ||
                 reading.HardwareName.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                 reading.HardwareName.Contains("SSD", StringComparison.OrdinalIgnoreCase));

            // Samsung NVMe controller hotspot runs normally between 60°C and 78°C.
            // Official Samsung operating spec warns at 82-85°C and throttles at 90-105°C.
            double warnLimit = reading.HardwareType switch
            {
                "Cpu" => Settings.CpuWarning,
                "Gpu" => Settings.GpuWarning,
                "Storage" => isStorageHotspot 
                    ? 95.0 
                    : (isSamsungNvme 
                        ? Math.Max(82.0, Settings.StorageWarning) 
                        : (isNvmeDrive ? Math.Max(72.0, Settings.StorageWarning) : Settings.StorageWarning)),
                _ => 85.0
            };

            double critLimit = reading.HardwareType switch
            {
                "Cpu" => Settings.CpuCritical,
                "Gpu" => Settings.GpuCritical,
                "Storage" => isStorageHotspot 
                    ? 105.0 
                    : (isSamsungNvme 
                        ? Math.Max(90.0, Settings.StorageCritical) 
                        : (isNvmeDrive ? Math.Max(82.0, Settings.StorageCritical) : Settings.StorageCritical)),
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
