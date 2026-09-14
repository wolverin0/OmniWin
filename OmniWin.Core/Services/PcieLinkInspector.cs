using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace OmniWin.Core.Services;

public class PcieDeviceReport
{
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceClass { get; set; } = string.Empty; // "GPU" or "NVMe Storage"
    public string DeviceId { get; set; } = string.Empty;
    public int CurrentLinkSpeedGen { get; set; } // 1=Gen 1, 2=Gen 2, 3=Gen 3, 4=Gen 4, 5=Gen 5
    public int MaxLinkSpeedGen { get; set; }
    public int CurrentLinkWidthLanes { get; set; } // 1, 2, 4, 8, 16
    public int MaxLinkWidthLanes { get; set; }
    public bool IsDegraded { get; set; }
    public string Status { get; set; } = "Desconocido";
    public string DiagnosticMessage { get; set; } = string.Empty;

    public string CurrentSpeedString => CurrentLinkSpeedGen > 0 ? $"PCIe {CurrentLinkSpeedGen}.0" : "N/A";
    public string MaxSpeedString => MaxLinkSpeedGen > 0 ? $"PCIe {MaxLinkSpeedGen}.0" : "N/A";
    public string CurrentWidthString => CurrentLinkWidthLanes > 0 ? $"x{CurrentLinkWidthLanes}" : "N/A";
    public string MaxWidthString => MaxLinkWidthLanes > 0 ? $"x{MaxLinkWidthLanes}" : "N/A";
}

public class PcieDoctorReport
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<PcieDeviceReport> Devices { get; set; } = new();
    public int DegradedDevicesCount => Devices.Count(d => d.IsDegraded);
    public bool HasIssues => DegradedDevicesCount > 0;
    public string Summary { get; set; } = string.Empty;
}

public class PcieLinkInspector
{
    private static readonly Lazy<PcieLinkInspector> _instance = new(() => new PcieLinkInspector());
    public static PcieLinkInspector Instance => _instance.Value;

    private static readonly string[] PcieKeys = new[]
    {
        "DEVPKEY_PciDevice_CurrentLinkSpeed",
        "DEVPKEY_PciDevice_CurrentLinkWidth",
        "DEVPKEY_PciDevice_MaxLinkSpeed",
        "DEVPKEY_PciDevice_MaxLinkWidth"
    };

    public PcieDoctorReport RunDoctorCheck()
    {
        var report = new PcieDoctorReport();

        try
        {
            // 1. Inspect GPUs (PNPClass = 'Display')
            var gpus = InspectPnpClass("Display", "GPU");
            report.Devices.AddRange(gpus);

            // 2. Inspect NVMe Controllers (PNPClass = 'SCSIAdapter')
            var storage = InspectPnpClass("SCSIAdapter", "NVMe Storage");
            report.Devices.AddRange(storage);

            if (report.DegradedDevicesCount == 0)
            {
                report.Summary = $"✓ Todos los enlaces PCIe analizados ({report.Devices.Count} dispositivos) están operando en su ancho de banda y velocidad máximos nominales.";
            }
            else
            {
                report.Summary = $"⚠ Se detectaron {report.DegradedDevicesCount} dispositivo(s) con enlace PCIe degradado. Revise ranuras compartidas M.2, cables riser o perfiles de ahorro de energía.";
            }
        }
        catch (Exception ex)
        {
            report.Summary = $"Error al inspeccionar enlaces PCIe: {ex.Message}";
        }

        return report;
    }

    private List<PcieDeviceReport> InspectPnpClass(string pnpClass, string category)
    {
        var results = new List<PcieDeviceReport>();

        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_PnPEntity WHERE PNPClass = '{pnpClass}'");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    string devId = obj["DeviceID"] as string ?? string.Empty;
                    if (!devId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string name = obj["Name"] as string ?? obj["Description"] as string ?? "Dispositivo PCI";

                    var props = ReadPcieProperties(obj);
                    if (!props.ContainsKey("DEVPKEY_PciDevice_MaxLinkWidth") || props["DEVPKEY_PciDevice_MaxLinkWidth"] == 0)
                        continue; // No es un dispositivo PCIe con reporte de enlace

                    int curSpeed = props.GetValueOrDefault("DEVPKEY_PciDevice_CurrentLinkSpeed", 0);
                    int maxSpeed = props.GetValueOrDefault("DEVPKEY_PciDevice_MaxLinkSpeed", 0);
                    int curWidth = props.GetValueOrDefault("DEVPKEY_PciDevice_CurrentLinkWidth", 0);
                    int maxWidth = props.GetValueOrDefault("DEVPKEY_PciDevice_MaxLinkWidth", 0);

                    bool isDegraded = (maxWidth > 0 && curWidth > 0 && curWidth < maxWidth) ||
                                      (maxSpeed > 0 && curSpeed > 0 && curSpeed < maxSpeed);

                    string status;
                    string diag;

                    if (isDegraded)
                    {
                        status = "Degradado";
                        var causes = new List<string>();
                        if (curWidth < maxWidth)
                        {
                            causes.Add($"Ancho de carril restringido a x{curWidth} (Capacidad física: x{maxWidth})");
                        }
                        if (curSpeed < maxSpeed)
                        {
                            causes.Add($"Velocidad negociada a Gen {curSpeed} (Capacidad física: Gen {maxSpeed})");
                        }
                        diag = string.Join("; ", causes) + ". Posibles causas: Ranura PCIe secundaria compartida con M.2 NVMe, cable Riser vertical degradado, o ahorro de energía ASPM activo en reposo.";
                    }
                    else
                    {
                        status = "Óptimo";
                        diag = $"Operando a máxima capacidad: PCIe Gen {curSpeed} @ x{curWidth} carriles.";
                    }

                    results.Add(new PcieDeviceReport
                    {
                        DeviceName = name,
                        DeviceClass = category,
                        DeviceId = devId,
                        CurrentLinkSpeedGen = curSpeed,
                        MaxLinkSpeedGen = maxSpeed,
                        CurrentLinkWidthLanes = curWidth,
                        MaxLinkWidthLanes = maxWidth,
                        IsDegraded = isDegraded,
                        Status = status,
                        DiagnosticMessage = diag
                    });
                }
            }
        }
        catch { }

        return results;
    }

    private Dictionary<string, int> ReadPcieProperties(ManagementObject obj)
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var inParams = obj.GetMethodParameters("GetDeviceProperties");
            inParams["devicePropertyKeys"] = PcieKeys;

            using var outParams = obj.InvokeMethod("GetDeviceProperties", inParams, null);
            if (outParams?["deviceProperties"] is ManagementBaseObject[] propArray)
            {
                foreach (var p in propArray)
                {
                    using (p)
                    {
                        string key = p["KeyName"] as string ?? string.Empty;
                        object? data = p["Data"];
                        if (data != null && int.TryParse(data.ToString(), out int intVal))
                        {
                            dict[key] = intVal;
                        }
                    }
                }
            }
        }
        catch { }

        return dict;
    }
}
