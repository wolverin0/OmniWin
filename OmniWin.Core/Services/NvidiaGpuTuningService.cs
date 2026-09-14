using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public enum NvidiaLowLatencyMode
{
    Off = 0,
    On = 1,
    Ultra = 2
}

public enum NvidiaPowerMode
{
    OptimalPower = 0,
    Adaptive = 1,
    PreferMaximumPerformance = 2
}

public class NvidiaGpuStatus
{
    public bool IsNvidiaGpuDetected { get; set; }
    public string GpuModel { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public string VbiosVersion { get; set; } = string.Empty;
    public string PciBusId { get; set; } = string.Empty;
    public string PciDeviceId { get; set; } = string.Empty;
    public string PciSubsystemId { get; set; } = string.Empty;
    public string SubsystemVendor { get; set; } = string.Empty;
    public int PcieGenCurrent { get; set; }
    public int PcieGenMax { get; set; }
    public int PcieGenHostMax { get; set; }
    public int PcieWidthCurrent { get; set; }
    public int PcieWidthMax { get; set; }
    public bool IsWidthBottlenecked => PcieWidthMax > 0 && PcieWidthCurrent > 0 && PcieWidthCurrent < PcieWidthMax;
    public int VramTotalMb { get; set; }
    public int Bar1TotalMb { get; set; }
    public int Bar1UsedMb { get; set; }
    public bool IsReBarHardwareActive => Bar1TotalMb >= 1024 || (VramTotalMb > 0 && Bar1TotalMb >= VramTotalMb);
    public string PcieLinkSummary => PcieWidthMax > 0 
        ? $"PCIe {PcieGenCurrent}.0 @ x{PcieWidthCurrent} (Capaz de Gen {PcieGenMax}.0 x{PcieWidthMax})"
        : "N/A";
    public string BottleneckNotice => IsWidthBottlenecked
        ? $"⚠️ Enlace operando a x{PcieWidthCurrent} de x{PcieWidthMax} posibles ({(PcieWidthCurrent * 100) / PcieWidthMax}% de líneas). Posible slot secundario, compartición de líneas con NVMe M.2 o modo de ahorro energético PCIe."
        : "✅ Enlace PCIe negociando al ancho máximo disponible.";
    public string ReBarStatusText => IsReBarHardwareActive
        ? $"✅ Habilitado en Hardware ({Bar1TotalMb} MiB direccionables directamente por CPU)"
        : $"❌ Deshabilitado en BIOS (Ventana Legacy de {Bar1TotalMb} MiB). Activa 'Above 4G Decoding' y 'Re-Size BAR' en tu BIOS/UEFI.";

    public bool IsReBarSupported { get; set; }
    public bool IsReBarEnabled { get; set; }
    public NvidiaLowLatencyMode LowLatencyMode { get; set; } = NvidiaLowLatencyMode.Off;
    public NvidiaPowerMode PowerMode { get; set; } = NvidiaPowerMode.OptimalPower;
    public int FrameRateLimiterFps { get; set; } = 0; // 0 = Unlimited
    public string StatusMessage { get; set; } = string.Empty;
}

public class NvidiaGpuTuningService
{
    public static NvidiaGpuTuningService Instance { get; } = new();

    private const string NvTweakRegistryPath = @"Software\NVIDIA Corporation\Global\NVTweak";
    private const string NvControlPanelRegistryPath = @"Software\NVIDIA Corporation\Global\Stereo3D";

    public NvidiaGpuStatus GetStatus()
    {
        var status = new NvidiaGpuStatus();

        try
        {
            // 1. Query deep PCIe & GPU details via nvidia-smi CSV
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,driver_version,vbios_version,pci.bus_id,pci.device_id,pci.sub_device_id,pcie.link.gen.gpucurrent,pcie.link.gen.max,pcie.link.gen.hostmax,pcie.link.width.current,pcie.link.width.max,memory.total --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            proc.Start();
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(1500);

            if (!string.IsNullOrWhiteSpace(output))
            {
                var parts = output.Split(',');
                if (parts.Length >= 2)
                {
                    status.IsNvidiaGpuDetected = true;
                    status.GpuModel = parts[0].Trim();
                    status.DriverVersion = parts[1].Trim();

                    if (parts.Length >= 3) status.VbiosVersion = parts[2].Trim();
                    if (parts.Length >= 4) status.PciBusId = parts[3].Trim();
                    if (parts.Length >= 5) status.PciDeviceId = parts[4].Trim();
                    if (parts.Length >= 6)
                    {
                        status.PciSubsystemId = parts[5].Trim();
                        status.SubsystemVendor = ResolveVendorFromSubsystemId(status.PciSubsystemId);
                    }
                    if (parts.Length >= 7 && int.TryParse(parts[6].Trim(), out int genCurr)) status.PcieGenCurrent = genCurr;
                    if (parts.Length >= 8 && int.TryParse(parts[7].Trim(), out int genMax)) status.PcieGenMax = genMax;
                    if (parts.Length >= 9 && int.TryParse(parts[8].Trim(), out int genHost)) status.PcieGenHostMax = genHost;
                    if (parts.Length >= 10 && int.TryParse(parts[9].Trim(), out int widthCurr)) status.PcieWidthCurrent = widthCurr;
                    if (parts.Length >= 11 && int.TryParse(parts[10].Trim(), out int widthMax)) status.PcieWidthMax = widthMax;
                    if (parts.Length >= 12 && int.TryParse(parts[11].Trim(), out int vramTotal)) status.VramTotalMb = vramTotal;
                }
            }

            // 2. Query BAR1 (Resizable BAR) memory aperture via nvidia-smi -q -i 0 -d MEMORY
            QueryBar1Memory(status);
        }
        catch
        {
            status.IsNvidiaGpuDetected = false;
        }

        if (!status.IsNvidiaGpuDetected)
        {
            status.StatusMessage = "No se detectó GPU NVIDIA en este sistema.";
            return status;
        }

        // 3. Read Registry Tweaks
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(NvTweakRegistryPath, writable: false);
            if (key != null)
            {
                object? llValue = key.GetValue("UltraLowLatencyMode");
                if (llValue is int llInt && Enum.IsDefined(typeof(NvidiaLowLatencyMode), llInt))
                {
                    status.LowLatencyMode = (NvidiaLowLatencyMode)llInt;
                }

                object? pwrValue = key.GetValue("PowerMizerMode");
                if (pwrValue is int pwrInt && Enum.IsDefined(typeof(NvidiaPowerMode), pwrInt))
                {
                    status.PowerMode = (NvidiaPowerMode)pwrInt;
                }

                object? fpsValue = key.GetValue("FrameRateLimiterFps");
                if (fpsValue is int fpsInt)
                {
                    status.FrameRateLimiterFps = Math.Max(0, fpsInt);
                }
            }

            // Check ReBAR support & active state
            status.IsReBarSupported = status.GpuModel.Contains("RTX 30", StringComparison.OrdinalIgnoreCase) ||
                                      status.GpuModel.Contains("RTX 40", StringComparison.OrdinalIgnoreCase) ||
                                      status.GpuModel.Contains("RTX 50", StringComparison.OrdinalIgnoreCase);
            status.IsReBarEnabled = status.IsReBarHardwareActive;

            status.StatusMessage = $"GPU {status.GpuModel} activa (Driver {status.DriverVersion} | VBIOS {status.VbiosVersion}).";
        }
        catch (Exception ex)
        {
            status.StatusMessage = $"GPU detectada con advertencia de configuración: {ex.Message}";
        }

        return status;
    }

    private static void QueryBar1Memory(NvidiaGpuStatus status)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "-q -i 0 -d MEMORY",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            proc.Start();
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);

            if (!string.IsNullOrWhiteSpace(output))
            {
                // Parse "BAR1 Memory Usage\n        Total : 256 MiB\n        Used : 227 MiB"
                int barIdx = output.IndexOf("BAR1 Memory Usage", StringComparison.OrdinalIgnoreCase);
                if (barIdx >= 0)
                {
                    string barSection = output.Substring(barIdx);
                    var lines = barSection.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Contains("Total") && line.Contains("MiB"))
                        {
                            var numStr = ExtractFirstDigits(line);
                            if (int.TryParse(numStr, out int bTotal)) status.Bar1TotalMb = bTotal;
                        }
                        else if (line.Contains("Used") && line.Contains("MiB"))
                        {
                            var numStr = ExtractFirstDigits(line);
                            if (int.TryParse(numStr, out int bUsed)) status.Bar1UsedMb = bUsed;
                        }
                        else if (line.Contains("Conf Compute") || line.Contains("Free"))
                        {
                            if (status.Bar1TotalMb > 0 && status.Bar1UsedMb > 0) break;
                        }
                    }
                }
            }
        }
        catch
        {
            // Silently fallback
        }
    }

    private static string ExtractFirstDigits(string line)
    {
        var colonIdx = line.IndexOf(':');
        if (colonIdx >= 0)
        {
            var valuePart = line.Substring(colonIdx + 1).Trim();
            var tokens = valuePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0) return tokens[0].Trim();
        }
        return string.Empty;
    }

    public static string ResolveVendorFromSubsystemId(string subsysId)
    {
        if (string.IsNullOrWhiteSpace(subsysId)) return "Desconocido";

        // Expected format: "0x161219DA" where last 4 hex characters are the Subsystem Vendor ID
        string clean = subsysId.Trim();
        if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(2);
        }

        if (clean.Length >= 4)
        {
            string vendorCode = clean.Substring(clean.Length - 4).ToUpperInvariant();
            return vendorCode switch
            {
                "10DE" => "NVIDIA (Founders Edition)",
                "19DA" => "ZOTAC Technology",
                "1043" => "ASUS (Republic of Gamers / TUF)",
                "1462" => "MSI (Micro-Star International)",
                "1458" => "GIGABYTE / AORUS",
                "3842" => "EVGA Corporation",
                "1569" => "Palit Microsystems",
                "1DA8" => "Gainward",
                "1849" => "ASRock",
                "174B" => "Sapphire Technology",
                "1682" => "XFX",
                "7377" => "Colorful (iGame)",
                "154B" => "PNY Technologies",
                "1B4C" => "GALAX / KFA2",
                "1E3B" => "Inno3D",
                "1028" => "Dell / Alienware",
                "103C" => "HP (Hewlett-Packard)",
                "17AA" => "Lenovo",
                _ => $"OEM (0x{vendorCode})"
            };
        }

        return "OEM / Genérico";
    }

    public bool SetLowLatencyMode(NvidiaLowLatencyMode mode)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(NvTweakRegistryPath, writable: true);
            key.SetValue("UltraLowLatencyMode", (int)mode, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SetPowerManagementMode(NvidiaPowerMode mode)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(NvTweakRegistryPath, writable: true);
            key.SetValue("PowerMizerMode", (int)mode, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SetFrameRateLimiter(int maxFps)
    {
        try
        {
            int clamped = Math.Clamp(maxFps, 0, 500);
            using var key = Registry.CurrentUser.CreateSubKey(NvTweakRegistryPath, writable: true);
            key.SetValue("FrameRateLimiterFps", clamped, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
