using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public enum GpuVendorType
{
    Unknown,
    Nvidia,
    AmdRadeon, // AMD / ATI
    IntelArc
}

public class UniversalGpuInfo
{
    public string Name { get; set; } = "GPU del Sistema";
    public GpuVendorType VendorType { get; set; } = GpuVendorType.Unknown;
    public string VendorName { get; set; } = "Desconocido";
    public string DriverVersion { get; set; } = string.Empty;
    public string VbiosVersion { get; set; } = string.Empty;
    public string PciSlotBusId { get; set; } = string.Empty;
    public int VramTotalMb { get; set; }
    public bool IsPrimaryHighPerformance { get; set; }

    // ReBAR / AMD Smart Access Memory (SAM)
    public string ReBarTechnologyName => VendorType switch
    {
        GpuVendorType.AmdRadeon => "AMD Smart Access Memory (SAM)",
        GpuVendorType.IntelArc => "Intel Resizable BAR",
        _ => "Resizable BAR (ReBAR)"
    };
    public bool IsReBarSupported { get; set; }
    public bool IsReBarActive { get; set; }
    public string ReBarStatusMessage { get; set; } = string.Empty;

    // Negociación de enlace PCIe
    public int PcieGenCurrent { get; set; } = 4;
    public int PcieGenMax { get; set; } = 4;
    public int PcieWidthCurrent { get; set; } = 16;
    public int PcieWidthMax { get; set; } = 16;
    public bool IsLinkWidthBottlenecked => PcieWidthMax > 0 && PcieWidthCurrent > 0 && PcieWidthCurrent < PcieWidthMax;
    public bool IsGenReducedForPowerSavings => PcieGenMax > 1 && PcieGenCurrent == 1;
    public string PcieLinkSummary
    {
        get
        {
            string genDesc = IsGenReducedForPowerSavings ? $"PCIe {PcieGenCurrent}.0 (Reposo ASPM)" : $"PCIe {PcieGenCurrent}.0";
            string widthDesc = IsLinkWidthBottlenecked ? $"@ x{PcieWidthCurrent} [⚠ Carril reducido: {PcieWidthCurrent}/{PcieWidthMax}]" : $"@ x{PcieWidthCurrent}";
            return $"{genDesc} {widthDesc} (Capacidad: Gen {PcieGenMax}.0 x{PcieWidthMax})";
        }
    }
    public string PcieLinkColorHex => IsLinkWidthBottlenecked ? (PcieWidthCurrent <= 4 ? "#EF4444" : "#F59E0B") : "#38BDF8";

    public string LatencyTweakName => VendorType switch
    {
        GpuVendorType.AmdRadeon => "AMD Radeon Anti-Lag / Radeon Chill",
        GpuVendorType.IntelArc => "Intel Low Latency Gaming Mode",
        _ => "NVIDIA Ultra Low Latency Mode (Reflex)"
    };
}

public class UniversalGpuService
{
    public static UniversalGpuService Instance { get; } = new();

    public List<UniversalGpuInfo> GetInstalledGpus()
    {
        var gpus = new List<UniversalGpuInfo>();

        try
        {
            // 1. Detectar GPUs mediante WMI / CIM
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                string name = (obj["Name"]?.ToString() ?? string.Empty).Trim();
                string pnpId = (obj["PNPDeviceID"]?.ToString() ?? string.Empty).Trim();
                string driver = (obj["DriverVersion"]?.ToString() ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(name)) continue;

                var gpu = new UniversalGpuInfo
                {
                    Name = name,
                    DriverVersion = driver
                };

                // Identificar Vendor ID
                if (pnpId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    gpu.VendorType = GpuVendorType.Nvidia;
                    gpu.VendorName = "NVIDIA";
                    gpu.IsPrimaryHighPerformance = true;
                }
                else if (pnpId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                {
                    gpu.VendorType = GpuVendorType.AmdRadeon;
                    gpu.VendorName = "AMD Radeon (ATI)";
                    gpu.IsPrimaryHighPerformance = !gpus.Exists(g => g.VendorType == GpuVendorType.Nvidia);
                }
                else if (pnpId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) || name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                {
                    gpu.VendorType = GpuVendorType.IntelArc;
                    gpu.VendorName = name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ? "Intel Arc" : "Intel UHD / Iris";
                    gpu.IsPrimaryHighPerformance = !gpus.Exists(g => g.VendorType == GpuVendorType.Nvidia || g.VendorType == GpuVendorType.AmdRadeon);
                }

                // VRAM
                if (obj["AdapterRAM"] != null && long.TryParse(obj["AdapterRAM"].ToString(), out long bytesRam) && bytesRam > 0)
                {
                    gpu.VramTotalMb = (int)(bytesRam / (1024 * 1024));
                }

                // Si es NVIDIA, enriquecer con los datos de NvidiaGpuTuningService
                if (gpu.VendorType == GpuVendorType.Nvidia)
                {
                    try
                    {
                        var nvStatus = NvidiaGpuTuningService.Instance.GetStatus();
                        if (nvStatus.IsNvidiaGpuDetected)
                        {
                            gpu.Name = nvStatus.GpuModel;
                            gpu.DriverVersion = nvStatus.DriverVersion;
                            gpu.VbiosVersion = nvStatus.VbiosVersion;
                            gpu.PciSlotBusId = nvStatus.PciBusId;
                            gpu.PcieGenCurrent = nvStatus.PcieGenCurrent;
                            gpu.PcieGenMax = nvStatus.PcieGenMax;
                            gpu.PcieWidthCurrent = nvStatus.PcieWidthCurrent;
                            gpu.PcieWidthMax = nvStatus.PcieWidthMax;
                            gpu.VramTotalMb = nvStatus.VramTotalMb > 0 ? nvStatus.VramTotalMb : gpu.VramTotalMb;
                            gpu.IsReBarSupported = nvStatus.IsReBarSupported;
                            gpu.IsReBarActive = nvStatus.IsReBarHardwareActive;
                            gpu.ReBarStatusMessage = nvStatus.ReBarStatusText;
                        }
                    }
                    catch { }
                }
                else if (gpu.VendorType == GpuVendorType.AmdRadeon)
                {
                    // Diagnóstico para AMD Radeon / ATI
                    gpu.IsReBarSupported = name.Contains("RX 6", StringComparison.OrdinalIgnoreCase) ||
                                           name.Contains("RX 7", StringComparison.OrdinalIgnoreCase) ||
                                           name.Contains("RX 8", StringComparison.OrdinalIgnoreCase);
                    gpu.IsReBarActive = gpu.IsReBarSupported; // Por defecto si el driver lo expone
                    gpu.ReBarStatusMessage = gpu.IsReBarSupported
                        ? "Compatible con AMD Smart Access Memory (SAM)"
                        : "SAM requiere serie RX 6000 o superior";
                    gpu.VbiosVersion = "Firmware AMD Adrenalin";
                    gpu.PciSlotBusId = "PCIe Bus Primario";
                }
                else if (gpu.VendorType == GpuVendorType.IntelArc)
                {
                    gpu.IsReBarSupported = true;
                    gpu.ReBarStatusMessage = name.Contains("Arc", StringComparison.OrdinalIgnoreCase)
                        ? "Intel ReBAR es MANDATORIO para rendimiento óptimo de Arc"
                        : "GPU integrada de soporte";
                    gpu.VbiosVersion = "GOP Driver Intel";
                    gpu.PciSlotBusId = "Bus Integrado / Slot";
                }

                gpus.Add(gpu);
            }
        }
        catch { }

        // Fallback si WMI falla
        if (gpus.Count == 0)
        {
            var nv = NvidiaGpuTuningService.Instance.GetStatus();
            if (nv.IsNvidiaGpuDetected)
            {
                gpus.Add(new UniversalGpuInfo
                {
                    Name = nv.GpuModel,
                    VendorType = GpuVendorType.Nvidia,
                    VendorName = "NVIDIA",
                    DriverVersion = nv.DriverVersion,
                    VbiosVersion = nv.VbiosVersion,
                    PciSlotBusId = nv.PciBusId,
                    PcieGenCurrent = nv.PcieGenCurrent,
                    PcieGenMax = nv.PcieGenMax,
                    PcieWidthCurrent = nv.PcieWidthCurrent,
                    PcieWidthMax = nv.PcieWidthMax,
                    VramTotalMb = nv.VramTotalMb,
                    IsReBarSupported = nv.IsReBarSupported,
                    IsReBarActive = nv.IsReBarHardwareActive,
                    ReBarStatusMessage = nv.ReBarStatusText,
                    IsPrimaryHighPerformance = true
                });
            }
        }

        return gpus;
    }
}
