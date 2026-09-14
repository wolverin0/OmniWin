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

            details.LogicalProcessors = Environment.ProcessorCount;

            // Determinar núcleos físicos aproximados
            if (details.Vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                if (details.Name.Contains("12th", StringComparison.OrdinalIgnoreCase) ||
                    details.Name.Contains("13th", StringComparison.OrdinalIgnoreCase) ||
                    details.Name.Contains("14th", StringComparison.OrdinalIgnoreCase) ||
                    details.Name.Contains("14900", StringComparison.OrdinalIgnoreCase) ||
                    details.Name.Contains("13900", StringComparison.OrdinalIgnoreCase) ||
                    details.Name.Contains("13700", StringComparison.OrdinalIgnoreCase) ||
                    details.Name.Contains("14700", StringComparison.OrdinalIgnoreCase) ||
                    details.Name.Contains("12700", StringComparison.OrdinalIgnoreCase) ||
                    details.Name.Contains("12900", StringComparison.OrdinalIgnoreCase))
                {
                    details.ArchitectureType = CpuArchitectureType.IntelHybrid_P_E_Cores;
                }
                else
                {
                    details.ArchitectureType = CpuArchitectureType.Standard;
                }
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
            // Consultar Core Parking actual
            string outCp = RunPowerCfg($"/q scheme_current {SubgroupProcessor} {SettingCpMinCores}");
            if (outCp.Contains("Current AC Power Setting Index: 0x"))
            {
                string hex = outCp.Substring(outCp.IndexOf("Current AC Power Setting Index: 0x") + 34, 8).Trim();
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int cpVal))
                {
                    details.CoreParkingMinPercent = Math.Clamp(cpVal, 0, 100);
                }
            }

            // Consultar EPP actual
            string outEpp = RunPowerCfg($"/q scheme_current {SubgroupProcessor} {SettingPerfEpp}");
            if (outEpp.Contains("Current AC Power Setting Index: 0x"))
            {
                string hex = outEpp.Substring(outEpp.IndexOf("Current AC Power Setting Index: 0x") + 34, 8).Trim();
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int eppVal))
                {
                    details.EnergyPerformancePreference = Math.Clamp(eppVal, 0, 100);
                }
            }
        }
        catch { }
    }

    public bool ApplyGamingCpuTuning()
    {
        bool ok = true;
        // 1. Core parking a 100% (Desactivar estacionamiento de núcleos para que todos estén despiertos)
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingCpMinCores, 100);
        
        // 2. EPP a 0 (Máximo rendimiento inmediato sin demora de rampa de frecuencia)
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingPerfEpp, 0);

        // 3. Si es Intel Hybrid, priorizar P-Cores
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingHeteroPolicy, 0);

        // 4. Asegurar 100% de frecuencia máxima
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingProcThrottleMax, 100);

        // Activar cambios en el esquema actual
        RunPowerCfg("/setactive scheme_current");
        return ok;
    }

    public bool ApplyBalancedCpuTuning()
    {
        bool ok = true;
        // Core parking balanceado (50%)
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingCpMinCores, 50);

        // EPP a 50 (Transición equilibrada entre ahorro y velocidad)
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingPerfEpp, 50);

        // Heterogeneous policy balanceado
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingHeteroPolicy, 1);

        // Mínimo 5%, Máximo 100%
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingProcThrottleMin, 5);
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingProcThrottleMax, 100);

        RunPowerCfg("/setactive scheme_current");
        return ok;
    }

    public bool ApplyEcoSilentCpuTuning()
    {
        bool ok = true;
        // Core parking conservador
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingCpMinCores, 25);

        // EPP al 80-100% (Favorece temperaturas frías y ventiladores en reposo)
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingPerfEpp, 80);

        // Limitar CPU al 99% (desactiva Intel Turbo Boost o AMD Precision Boost agresivo)
        ok &= SetPowerSettingIndex(SubgroupProcessor, SettingProcThrottleMax, 99);

        RunPowerCfg("/setactive scheme_current");
        return ok;
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

            RunPowerCfg(argsAc);
            RunPowerCfg(argsDc);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string RunPowerCfg(string arguments)
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
            proc.WaitForExit(1500);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }
}
