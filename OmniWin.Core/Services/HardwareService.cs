using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public class HardwareSensorData
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double? Value { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public class HardwareComponentInfo
{
    public string Name { get; set; } = string.Empty;
    public string HardwareType { get; set; } = string.Empty;
    public List<HardwareSensorData> Sensors { get; set; } = new();
}

public class HardwareTelemetrySnapshot
{
    public string CpuName { get; set; } = "Desconocido";
    public double? CpuLoadPercent { get; set; }
    public double? CpuTemperatureCelsius { get; set; }
    public double? CpuPowerWatts { get; set; }

    public string GpuName { get; set; } = "Desconocido";
    public double? GpuLoadPercent { get; set; }
    public double? GpuTemperatureCelsius { get; set; }
    public double? GpuMemoryUsedMB { get; set; }

    public double? RamUsedGB { get; set; }
    public double? RamTotalGB { get; set; }
    public double? RamLoadPercent { get; set; }

    public List<HardwareComponentInfo> Components { get; set; } = new();
}

public class HardwareService : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    private static long _prevIdleTime;
    private static long _prevKernelTime;
    private static long _prevUserTime;
    private static bool _firstSample = true;

    private static double? GetCpuLoadFromSystemTimes()
    {
        try
        {
            if (!GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
                return null;

            if (_firstSample)
            {
                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;
                _firstSample = false;
                return null;
            }

            long usr = userTime - _prevUserTime;
            long ker = kernelTime - _prevKernelTime;
            long idl = idleTime - _prevIdleTime;

            _prevIdleTime = idleTime;
            _prevKernelTime = kernelTime;
            _prevUserTime = userTime;

            long total = usr + ker;
            if (total <= 0) return 0;

            double load = (double)(total - idl) / total * 100.0;
            return Math.Clamp(load, 0.0, 100.0);
        }
        catch
        {
            return null;
        }
    }

    public HardwareService()
    {
    }

    public HardwareTelemetrySnapshot GetTelemetrySnapshot()
    {
        var snapshot = new HardwareTelemetrySnapshot();

        // 1. CPU Name via Registry or Fallback
        string? regCpu = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            regCpu = key?.GetValue("ProcessorNameString")?.ToString();
        }
        catch { }
        snapshot.CpuName = !string.IsNullOrWhiteSpace(regCpu)
            ? regCpu.Trim()
            : (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Procesador del Sistema");

        // 2. CPU Load via kernel32!GetSystemTimes
        snapshot.CpuLoadPercent = GetCpuLoadFromSystemTimes();

        // 3. GPU Name via Registry
        string? regGpu = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
            regGpu = key?.GetValue("DriverDesc")?.ToString();
        }
        catch { }
        snapshot.GpuName = !string.IsNullOrWhiteSpace(regGpu) ? regGpu.Trim() : "GPU del Sistema";

        // 4. Memory metrics via GlobalMemoryStatusEx
        try
        {
            var mem = new MemoryService().GetMemoryStats();
            snapshot.RamLoadPercent = mem.UsagePercentage;
            snapshot.RamUsedGB = Math.Round(mem.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2);
            snapshot.RamTotalGB = Math.Round(mem.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2);
        }
        catch { }

        // 5. Thermal & Sensor Telemetry (CPU & GPU Temperatures)
        try
        {
            var thermalSnap = ThermalSensorService.Instance.GetSnapshot();
            snapshot.CpuTemperatureCelsius = thermalSnap.CpuPackageTemp;
            snapshot.GpuTemperatureCelsius = thermalSnap.GpuCoreTemp;
            if (thermalSnap.Temperatures.Any(t => t.HardwareType == "Gpu"))
            {
                var gpuTemp = thermalSnap.Temperatures.First(t => t.HardwareType == "Gpu");
                if (!string.IsNullOrWhiteSpace(gpuTemp.HardwareName) && (snapshot.GpuName == "GPU del Sistema" || snapshot.GpuName == "Desconocido"))
                {
                    snapshot.GpuName = gpuTemp.HardwareName;
                }
            }
        }
        catch { }

        // Populate component info safely
        var cpuComp = new HardwareComponentInfo
        {
            Name = snapshot.CpuName,
            HardwareType = "Cpu"
        };
        if (snapshot.CpuLoadPercent.HasValue)
        {
            cpuComp.Sensors.Add(new HardwareSensorData
            {
                Name = "CPU Total Load",
                Type = "Load",
                Value = snapshot.CpuLoadPercent.Value,
                Unit = "%"
            });
        }
        snapshot.Components.Add(cpuComp);

        var ramComp = new HardwareComponentInfo
        {
            Name = "Memoria del Sistema",
            HardwareType = "Memory"
        };
        if (snapshot.RamLoadPercent.HasValue)
        {
            ramComp.Sensors.Add(new HardwareSensorData
            {
                Name = "Memory Used Percent",
                Type = "Load",
                Value = snapshot.RamLoadPercent.Value,
                Unit = "%"
            });
        }
        if (snapshot.RamUsedGB.HasValue)
        {
            ramComp.Sensors.Add(new HardwareSensorData
            {
                Name = "Memory Used",
                Type = "Data",
                Value = snapshot.RamUsedGB.Value,
                Unit = "GB"
            });
        }
        snapshot.Components.Add(ramComp);

        var gpuComp = new HardwareComponentInfo
        {
            Name = snapshot.GpuName,
            HardwareType = "Gpu"
        };
        snapshot.Components.Add(gpuComp);

        return snapshot;
    }

    public void Dispose()
    {
    }
}
