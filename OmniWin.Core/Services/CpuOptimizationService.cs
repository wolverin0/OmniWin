using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public enum CpuArchitectureType
{
    Standard,
    IntelHybrid_P_E_Cores, // Intel 12th, 13th, 14th, 15th Gen (P-Cores + E-Cores)
    AmdRyzen_Standard,
    AmdRyzen_3D_VCache     // AMD Ryzen X3D (7800X3D, 7900X3D, 7950X3D, etc.)
}

public class CpuDetails
{
    public string Name { get; set; } = "Desconocido";
    public string Vendor { get; set; } = "Desconocido"; // Intel / AMD
    public int PhysicalCores { get; set; }
    public int LogicalProcessors { get; set; }
    public CpuArchitectureType ArchitectureType { get; set; } = CpuArchitectureType.Standard;
    public bool IsIntelHybrid => ArchitectureType == CpuArchitectureType.IntelHybrid_P_E_Cores;
    public bool IsAmdX3D => ArchitectureType == CpuArchitectureType.AmdRyzen_3D_VCache;

    // Estado actual de optimizaciones
    public int CoreParkingMinPercent { get; set; } = 100; // 100 = Todos los núcleos activos (Core Parking OFF)
    public int EnergyPerformancePreference { get; set; } = 50; // 0 = Max Perf, 50 = Balanced, 100 = Max Power Save
    public int HeterogeneousPolicy { get; set; } = 0; // 0 = Prefer Performance Cores
    public int MaxProcessorFrequencyPercent { get; set; } = 100;
    public int MinProcessorFrequencyPercent { get; set; } = 5;
}

public class CpuOptimizationService
{
    public static CpuOptimizationService Instance { get; } = new();

    // GUIDs de Power Settings para Subgroup Procesador en Windows
    private const string SubgroupProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string SettingCpMinCores = "0cc5b647-c36e-4684-81f6-02e0eb6e0d35"; // Core Parking Min Cores
    private const string SettingPerfEpp = "3668d466-4f4d-434b-ac50-1092795f7d26";    // Energy Performance Preference (EPP)
    private const string SettingHeteroPolicy = "7f2f5c9f-3fc4-47e6-9e49-f595f32b0092"; // Heterogeneous Policy (P vs E cores)
    private const string SettingProcThrottleMin = "893dee8e-2bef-41e0-89c6-b55d0929964c"; // Min CPU State
    private const string SettingProcThrottleMax = "bc5038f7-23e0-4960-96da-33abaf5935ec"; // Max CPU State

    public CpuDetails GetCpuDetails()
    {
        var details = new CpuDetails();

        try
        {
            // 1. Obtener información de CPU desde el Registro de Windows
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key != null)
            {
                details.Name = (key.GetValue("ProcessorNameString") as string ?? "Desconocido").Trim();
                details.Vendor = (key.GetValue("VendorIdentifier") as string ?? "Desconocido").Trim();
            }

            var topo = CpuTopologyService.Instance.GetTopology();
            details.LogicalProcessors = topo.LogicalProcessorCount;
            details.PhysicalCores = topo.PhysicalCoreCount;

            // Determinar arquitectura de núcleos físicos
            if (topo.IsHybrid)
            {
                details.ArchitectureType = CpuArchitectureType.IntelHybrid_P_E_Cores;
            }
            else if (details.Vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase))
            {
                if (details.Name.Contains("X3D", StringComparison.OrdinalIgnoreCase))
                {
                    details.ArchitectureType = CpuArchitectureType.AmdRyzen_3D_VCache;
                }
                else
                {
                    details.ArchitectureType = CpuArchitectureType.AmdRyzen_Standard;
                }
            }
            else
            {
                details.ArchitectureType = CpuArchitectureType.Standard;
            }

            // 2. Consultar ajustes de energía de CPU actuales
            ReadCurrentCpuPowerValues(details);
        }
        catch { }

        return details;
    }

    private void ReadCurrentCpuPowerValues(CpuDetails details)
    {
        try
        {
            // Consultar Core Parking actual (compatible con Windows en inglés, español y cualquier idioma)
            var (outCp, _) = RunPowerCfg($"/q scheme_current {SubgroupProcessor} {SettingCpMinCores}");
            int? cpVal = ExtractHexSetting(outCp);
            if (cpVal.HasValue)
            {
                details.CoreParkingMinPercent = Math.Clamp(cpVal.Value, 0, 100);
            }

            // Consultar EPP actual
            var (outEpp, _) = RunPowerCfg($"/q scheme_current {SubgroupProcessor} {SettingPerfEpp}");
            int? eppVal = ExtractHexSetting(outEpp);
            if (eppVal.HasValue)
            {
                details.EnergyPerformancePreference = Math.Clamp(eppVal.Value, 0, 100);
            }
        }
        catch { }
    }

    private static int? ExtractHexSetting(string powerCfgOutput)
    {
        if (string.IsNullOrWhiteSpace(powerCfgOutput)) return null;

        // Busca la línea de corriente alterna (AC / CA) independientemente del idioma del SO
        var lines = powerCfgOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Contains("AC", StringComparison.OrdinalIgnoreCase) || line.Contains("CA", StringComparison.OrdinalIgnoreCase))
            {
                int hexIdx = line.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
                if (hexIdx >= 0 && hexIdx + 2 < line.Length)
                {
                    string hexSub = line.Substring(hexIdx + 2).Trim();
                    string hex = new string(hexSub.TakeWhile(c => Uri.IsHexDigit(c)).ToArray());
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int val))
                    {
                        return val;
                    }
                }
            }
        }
        return null;
    }

    public bool ApplyGamingCpuTuning()
    {
        bool anyApplied = false;
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingCpMinCores, 100);
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingPerfEpp, 0);
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingHeteroPolicy, 0);
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingProcThrottleMax, 100);

        var (_, exitCode) = RunPowerCfg("/setactive scheme_current");
        return anyApplied || exitCode == 0;
    }

    public bool ApplyBalancedCpuTuning()
    {
        bool anyApplied = false;
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingCpMinCores, 50);
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingPerfEpp, 50);
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingHeteroPolicy, 1);
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingProcThrottleMin, 5);
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingProcThrottleMax, 100);

        var (_, exitCode) = RunPowerCfg("/setactive scheme_current");
        return anyApplied || exitCode == 0;
    }

    public bool ApplyEcoSilentCpuTuning()
    {
        bool anyApplied = false;
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingCpMinCores, 25);
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingPerfEpp, 80);
        anyApplied |= SetPowerSettingIndex(SubgroupProcessor, SettingProcThrottleMax, 99);

        var (_, exitCode) = RunPowerCfg("/setactive scheme_current");
        return anyApplied || exitCode == 0;
    }

    public bool SetCoreParkingMinPercent(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        bool ok = SetPowerSettingIndex(SubgroupProcessor, SettingCpMinCores, clamped);
        RunPowerCfg("/setactive scheme_current");
        return ok;
    }

    public bool SetEnergyPerformancePreference(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        bool ok = SetPowerSettingIndex(SubgroupProcessor, SettingPerfEpp, clamped);
        RunPowerCfg("/setactive scheme_current");
        return ok;
    }

    private static bool SetPowerSettingIndex(string subgroup, string setting, int value)
    {
        try
        {
            string argsAc = $"/setacvalueindex scheme_current {subgroup} {setting} {value}";
            string argsDc = $"/setdcvalueindex scheme_current {subgroup} {setting} {value}";

            var (_, exitAc) = RunPowerCfg(argsAc);
            var (_, exitDc) = RunPowerCfg(argsDc);
            return exitAc == 0 || exitDc == 0;
        }
        catch
        {
            return false;
        }
    }

    private static (string output, int exitCode) RunPowerCfg(string arguments)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            proc.Start();
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            return (output, proc.ExitCode);
        }
        catch
        {
            return (string.Empty, -1);
        }
    }
}
