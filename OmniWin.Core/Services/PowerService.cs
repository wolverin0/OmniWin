using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class PowerSchemeInfo
{
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class PowerService
{
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryTimerResolution(out uint MinimumResolution, out uint MaximumResolution, out uint CurrentResolution);

    public (double minMs, double maxMs, double currentMs) GetTimerResolution()
    {
        try
        {
            if (NtQueryTimerResolution(out uint min, out uint max, out uint cur) == 0)
            {
                return (min / 10000.0, max / 10000.0, cur / 10000.0);
            }
        }
        catch { }
        return (15.6, 0.5, 1.0);
    }

    public (bool success, double newResolutionMs) SetHighPrecisionTimer(bool enable05Ms = true)
    {
        try
        {
            // 5000 units = 0.5 ms; 10000 = 1.0 ms
            uint desired = enable05Ms ? 5000u : 10000u;
            int status = NtSetTimerResolution(desired, true, out uint current);
            return (status == 0, current / 10000.0);
        }
        catch
        {
            return (false, 1.0);
        }
    }

    public async Task<List<PowerSchemeInfo>> GetPowerSchemesAsync()
    {
        var list = new List<PowerSchemeInfo>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "/list",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return list;

            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var matches = Regex.Matches(output, @"GUID:\s+([a-f0-9\-]+)\s+\(([^)]+)\)(\s+\*)?");
            foreach (Match m in matches)
            {
                list.Add(new PowerSchemeInfo
                {
                    Guid = m.Groups[1].Value,
                    Name = m.Groups[2].Value,
                    IsActive = m.Groups[3].Success && m.Groups[3].Value.Contains("*")
                });
            }
        }
        catch { }

        return list;
    }

    public async Task<bool> SetActiveSchemeAsync(string schemeGuid)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = $"/setactive {schemeGuid}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task<string> UnlockUltimatePerformanceSchemeAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "Error ejecutando powercfg";
            string outStr = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return outStr.Trim();
        }
        catch (Exception ex)
        {
            return $"Error desbloqueando plan de energía: {ex.Message}";
        }
    }

    public PowerProfileRecommendation GetProfileRecommendation(string schemeNameOrGuid)
    {
        string name = (schemeNameOrGuid ?? string.Empty).ToLowerInvariant();
        var rec = new PowerProfileRecommendation();

        if (name.Contains("azurite") || name.Contains("ultimate") || name.Contains("high performance") || name.Contains("alto rendimiento"))
        {
            rec.TargetProfileName = "Rendimiento Extremo & Gaming (Baja Latencia)";
            rec.CpuCoreParkingRecommendation = "Core Parking al 100% (Desactivar estacionamiento de núcleos para eliminar micro-tirones)";
            rec.EnergyPreferenceRecommendation = "EPP = 0% (Frecuencias instantáneas sin retardo de rampa)";
            rec.HeterogeneousRecommendation = "Priorizar P-Cores de alto rendimiento para juegos (Intel Thread Director) o CCD 3D V-Cache (AMD)";
            rec.TimerResolutionRecommendation = "Fijar Temporizador NT a 0.5 ms para mínima latencia de fotogramas";
            rec.ThermalCoolerRecommendation = "Activar auto-conmutación a Curva Gamer al detectar juegos en ejecución";
            rec.Highlights = new List<string>
            {
                "Elimina el stuttering provocado por el kernel durmiendo/despertando núcleos.",
                "Prioriza P-Cores sobre E-Cores para el proceso de juego en primer plano.",
                "Temporizador de Windows a 0.5ms para un frametime perfectamente estable."
            };
        }
        else if (name.Contains("power saver") || name.Contains("economizador") || name.Contains("ahorro"))
        {
            rec.TargetProfileName = "Silencio Extremo & Seguridad Térmica";
            rec.CpuCoreParkingRecommendation = "Core Parking dinámico (estacionar núcleos inactivos para bajar watts)";
            rec.EnergyPreferenceRecommendation = "EPP = 80-100% (Preferencia de energía y baja temperatura)";
            rec.HeterogeneousRecommendation = "Descargar tareas de fondo a E-Cores (Eficientes)";
            rec.TimerResolutionRecommendation = "Temporizador estándar de 15.6 ms o 1.0 ms";
            rec.ThermalCoolerRecommendation = "Curva Silenciosa (0 RPM fan stop por debajo de 55°C)";
            rec.Highlights = new List<string>
            {
                "Tope de CPU al 99% para evitar picos de 250W+ del Turbo Boost de fábrica.",
                "Mantiene el sistema en silencio acústico total con ventiladores apagados en reposo.",
                "Ideal para trabajo de oficina, lectura y descargas nocturnas."
            };
        }
        else
        {
            rec.TargetProfileName = "Equilibrado Inteligente (Uso Diario)";
            rec.CpuCoreParkingRecommendation = "Core Parking al 50% según demanda dinámica de hilos";
            rec.EnergyPreferenceRecommendation = "EPP = 50% (Transición suave entre consumo y frecuencia)";
            rec.HeterogeneousRecommendation = "Distribución estándar P-Cores / E-Cores gestionada por SO";
            rec.TimerResolutionRecommendation = "1.0 ms de precisión";
            rec.ThermalCoolerRecommendation = "Curva Equilibrada con histéresis de 3°C";
            rec.Highlights = new List<string>
            {
                "Balance óptimo entre velocidad y consumo energético.",
                "Frecuencia base baja a 800 MHz en reposo para silicio fresco.",
                "Ventiladores a baja velocidad o apagados según temperatura de silicio."
            };
        }

        return rec;
    }

    public async Task<bool> ApplyRecommendedTuningForSchemeAsync(string schemeNameOrGuid)
    {
        return await Task.Run(() =>
        {
            string name = (schemeNameOrGuid ?? string.Empty).ToLowerInvariant();
            if (name.Contains("azurite") || name.Contains("ultimate") || name.Contains("high performance") || name.Contains("alto rendimiento"))
            {
                SetHighPrecisionTimer(true);
                return CpuOptimizationService.Instance.ApplyGamingCpuTuning();
            }
            else if (name.Contains("power saver") || name.Contains("economizador") || name.Contains("ahorro"))
            {
                return CpuOptimizationService.Instance.ApplyEcoSilentCpuTuning();
            }
            else
            {
                return CpuOptimizationService.Instance.ApplyBalancedCpuTuning();
            }
        });
    }
}

public class PowerProfileRecommendation
{
    public string TargetProfileName { get; set; } = string.Empty;
    public string CpuCoreParkingRecommendation { get; set; } = string.Empty;
    public string EnergyPreferenceRecommendation { get; set; } = string.Empty;
    public string HeterogeneousRecommendation { get; set; } = string.Empty;
    public string TimerResolutionRecommendation { get; set; } = string.Empty;
    public string ThermalCoolerRecommendation { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
}
