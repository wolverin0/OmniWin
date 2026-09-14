using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public class MsiDeviceModel
{
    public string DeviceKeyPath { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string DeviceClass { get; set; } = string.Empty; // "Display", "Net", "SCSIAdapter", etc.
    public string Location { get; set; } = string.Empty;
    public bool MsiSupported { get; set; }
    public bool HasMsiKey { get; set; }
    public int MessageNumberLimit { get; set; } = 1;
    public string Priority { get; set; } = "Undefined"; // High, Normal, Low, Undefined
    public bool IsRecommended { get; set; }
    public string Status { get; set; } = "Desconocido";

    public string CategoryDisplayName => DeviceClass switch
    {
        "Display" => "GPU (Tarjeta Gráfica)",
        "Net" => "Red (Ethernet / Wi-Fi)",
        "SCSIAdapter" => "Almacenamiento (NVMe / SATA)",
        _ => DeviceClass
    };
}

public class MsiOptimizationReport
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<MsiDeviceModel> Devices { get; set; } = new();
    public int TotalDevices => Devices.Count;
    public int MsiActiveCount => Devices.Count(d => d.MsiSupported);
    public int LineBasedCount => Devices.Count(d => !d.MsiSupported);
    public int RecommendedToOptimizeCount => Devices.Count(d => d.IsRecommended && !d.MsiSupported);
    public string Summary { get; set; } = string.Empty;
}

public class MsiInterruptService
{
    private static readonly Lazy<MsiInterruptService> _instance = new(() => new MsiInterruptService());
    public static MsiInterruptService Instance => _instance.Value;

    private const string PciEnumRoot = @"SYSTEM\CurrentControlSet\Enum\PCI";

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public List<MsiDeviceModel> GetPciMsiDevices(bool filterGamingRelevantOnly = true)
    {
        var list = new List<MsiDeviceModel>();

        try
        {
            using var pciRoot = Registry.LocalMachine.OpenSubKey(PciEnumRoot);
            if (pciRoot == null) return list;

            string[] subKeys = pciRoot.GetSubKeyNames();
            foreach (string deviceId in subKeys)
            {
                using var devKey = pciRoot.OpenSubKey(deviceId);
                if (devKey == null) continue;

                string[] instanceIds = devKey.GetSubKeyNames();
                foreach (string instanceId in instanceIds)
                {
                    using var instKey = devKey.OpenSubKey(instanceId);
                    if (instKey == null) continue;

                    string deviceClass = instKey.GetValue("Class")?.ToString() ?? string.Empty;

                    // Filter for devices where MSI is impactful (GPUs, NICs, Storage)
                    if (filterGamingRelevantOnly)
                    {
                        if (!string.Equals(deviceClass, "Display", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(deviceClass, "Net", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(deviceClass, "SCSIAdapter", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    string rawDesc = instKey.GetValue("DeviceDesc")?.ToString() ?? string.Empty;
                    string friendly = instKey.GetValue("FriendlyName")?.ToString() ?? string.Empty;
                    string location = instKey.GetValue("LocationInformation")?.ToString() ?? string.Empty;

                    string finalName = CleanDeviceName(friendly, rawDesc, deviceId);
                    string keyPath = $@"{PciEnumRoot}\{deviceId}\{instanceId}";

                    var item = new MsiDeviceModel
                    {
                        DeviceKeyPath = keyPath,
                        DeviceId = deviceId,
                        InstanceId = instanceId,
                        FriendlyName = finalName,
                        DeviceClass = deviceClass,
                        Location = location,
                        IsRecommended = string.Equals(deviceClass, "Display", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(deviceClass, "Net", StringComparison.OrdinalIgnoreCase)
                    };

                    // Check MSI Properties
                    using var msiKey = instKey.OpenSubKey(@"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties");
                    if (msiKey != null)
                    {
                        item.HasMsiKey = true;
                        object? val = msiKey.GetValue("MSISupported");
                        if (val is int intVal)
                        {
                            item.MsiSupported = (intVal == 1);
                        }
                        else if (val is long longVal)
                        {
                            item.MsiSupported = (longVal == 1);
                        }

                        object? limitVal = msiKey.GetValue("MessageNumberLimit");
                        if (limitVal is int limitInt)
                        {
                            item.MessageNumberLimit = limitInt;
                        }
                    }

                    // Check Priority
                    using var affKey = instKey.OpenSubKey(@"Device Parameters\Interrupt Management\Affinity Policy");
                    if (affKey != null)
                    {
                        object? prio = affKey.GetValue("DevicePriority");
                        if (prio is int prioInt)
                        {
                            item.Priority = prioInt switch
                            {
                                1 => "Low",
                                2 => "High",
                                0 => "Normal",
                                3 => "Undefined",
                                _ => prioInt.ToString()
                            };
                        }
                    }

                    item.Status = item.MsiSupported
                        ? $"MSI Activo (Prioridad: {item.Priority})"
                        : "IRQ Clásico (Línea Compartida)";

                    list.Add(item);
                }
            }
        }
        catch { }

        return list.OrderByDescending(d => d.IsRecommended)
                   .ThenBy(d => d.DeviceClass)
                   .ThenBy(d => d.FriendlyName)
                   .ToList();
    }

    public MsiOptimizationReport RunDoctorReport()
    {
        var devices = GetPciMsiDevices(filterGamingRelevantOnly: true);
        var report = new MsiOptimizationReport
        {
            Devices = devices
        };

        if (report.RecommendedToOptimizeCount == 0)
        {
            report.Summary = $"✓ Todos los dispositivos críticos ({report.MsiActiveCount} GPU/Red) ya tienen MSI Mode activo con interrupciones dedicadas.";
        }
        else
        {
            report.Summary = $"⚠ Se detectaron {report.RecommendedToOptimizeCount} dispositivo(s) críticos operando en IRQ clásico compartido. Habilitar MSI Mode puede eliminar contención y picos de latencia DPC.";
        }

        return report;
    }

    public bool SetMsiMode(string deviceKeyPath, bool enable, string priority = "High")
    {
        if (!IsAdministrator())
        {
            throw new UnauthorizedAccessException("Se requieren privilegios de Administrador para modificar la configuración de interrupciones PCI/MSI.");
        }

        try
        {
            // Open device root key
            using var devKey = Registry.LocalMachine.OpenSubKey(deviceKeyPath, writable: true);
            if (devKey == null) return false;

            string devClass = devKey.GetValue("Class") as string ?? string.Empty;
            bool isNet = devClass.Equals("Net", StringComparison.OrdinalIgnoreCase);

            // 1. Configure MessageSignaledInterruptProperties
            using (var msiKey = devKey.CreateSubKey(@"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties", writable: true))
            {
                msiKey.SetValue("MSISupported", enable ? 1 : 0, RegistryValueKind.DWord);
                
                // Do NOT force MessageNumberLimit=1 on Network adapters: MSI-X requires multiple messages for RSS (Receive Side Scaling)
                if (enable && !isNet && msiKey.GetValue("MessageNumberLimit") == null)
                {
                    msiKey.SetValue("MessageNumberLimit", 1, RegistryValueKind.DWord);
                }
            }

            // 2. Configure Affinity Policy (DevicePriority)
            using (var affKey = devKey.CreateSubKey(@"Device Parameters\Interrupt Management\Affinity Policy", writable: true))
            {
                int prioVal = priority.ToLowerInvariant() switch
                {
                    "high" => 2,
                    "low" => 1,
                    "normal" => 0,
                    _ => 3 // Undefined
                };
                affKey.SetValue("DevicePriority", prioVal, RegistryValueKind.DWord);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public int OptimizeRecommendedGamingDevices()
    {
        var devices = GetPciMsiDevices(filterGamingRelevantOnly: true);
        int modifiedCount = 0;

        foreach (var dev in devices)
        {
            if (dev.IsRecommended && (!dev.MsiSupported || dev.Priority != "High"))
            {
                if (SetMsiMode(dev.DeviceKeyPath, enable: true, priority: "High"))
                {
                    modifiedCount++;
                }
            }
        }

        return modifiedCount;
    }

    private static string CleanDeviceName(string friendly, string rawDesc, string fallbackId)
    {
        if (!string.IsNullOrWhiteSpace(friendly)) return friendly;
        if (!string.IsNullOrWhiteSpace(rawDesc))
        {
            int idx = rawDesc.LastIndexOf(';');
            if (idx >= 0 && idx < rawDesc.Length - 1)
            {
                return rawDesc.Substring(idx + 1).Trim();
            }
            return rawDesc.Trim();
        }
        return fallbackId;
    }
}
