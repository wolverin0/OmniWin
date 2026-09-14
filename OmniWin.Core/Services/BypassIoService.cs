using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OmniWin.Core.Services;

public enum BypassIoStatus
{
    Supported,
    PartiallySupported,
    NotSupported,
    UnsupportedOsVersion,
    Unknown
}

public class VolumeBypassIoDetail
{
    public string Volume { get; set; } = string.Empty;
    public string DriveLabel { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public BypassIoStatus Status { get; set; } = BypassIoStatus.Unknown;
    public bool VolumeSupported { get; set; }
    public bool StorageDriverSupported { get; set; }
    public List<string> BlockingDrivers { get; set; } = new();
    public List<string> ReasonDetails { get; set; } = new();
    public string SummaryMessage { get; set; } = string.Empty;
}

public class DirectStorageDoctorReport
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsWindows11OrNewer { get; set; }
    public int WindowsBuild { get; set; }
    public List<VolumeBypassIoDetail> Volumes { get; set; } = new();
    public string OverallAssessment { get; set; } = string.Empty;
}

public class BypassIoService
{
    private static readonly Lazy<BypassIoService> _instance = new(() => new BypassIoService());
    public static BypassIoService Instance => _instance.Value;

    public DirectStorageDoctorReport CheckSystemBypassIoState()
    {
        var report = new DirectStorageDoctorReport
        {
            WindowsBuild = Environment.OSVersion.Version.Build,
            IsWindows11OrNewer = Environment.OSVersion.Version.Build >= 22000
        };

        if (!report.IsWindows11OrNewer)
        {
            report.OverallAssessment = $"BypassIO (aceleración hardware de DirectStorage) requiere Windows 11 (Build 22000 o superior). En este sistema (Windows 10 Build {report.WindowsBuild}), los juegos utilizan la pila tradicional de E/S NVMe estándar de alto rendimiento.";
            
            // Populate drives for information
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                report.Volumes.Add(new VolumeBypassIoDetail
                {
                    Volume = drive.Name,
                    DriveLabel = drive.VolumeLabel,
                    FileSystem = drive.DriveFormat,
                    Status = BypassIoStatus.UnsupportedOsVersion,
                    SummaryMessage = "Requiere Windows 11 para la ruta de bajo overhead BypassIO."
                });
            }
            return report;
        }

        // On Windows 11: Execute fsutil bypassIo state /v for each fixed drive
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var detail = CheckVolumeBypassIo(drive.Name);
            detail.DriveLabel = drive.VolumeLabel;
            detail.FileSystem = drive.DriveFormat;
            report.Volumes.Add(detail);
        }

        bool allSupported = report.Volumes.Any() && report.Volumes.All(v => v.Status == BypassIoStatus.Supported);
        if (allSupported)
        {
            report.OverallAssessment = "✓ DirectStorage BypassIO está 100% activo y optimizado en todos los volúmenes del sistema sin controladores bloqueantes.";
        }
        else
        {
            int blockedCount = report.Volumes.Count(v => v.Status != BypassIoStatus.Supported);
            report.OverallAssessment = $"⚠ Se identificaron {blockedCount} volumen(es) con BypassIO parcial o bloqueado por controladores/filtros de almacenamiento.";
        }

        return report;
    }

    public VolumeBypassIoDetail CheckVolumeBypassIo(string volumeOrPath)
    {
        string rootPath = volumeOrPath;
        try
        {
            rootPath = Path.GetPathRoot(volumeOrPath) ?? volumeOrPath;
        }
        catch { }

        var detail = new VolumeBypassIoDetail
        {
            Volume = rootPath
        };

        if (Environment.OSVersion.Version.Build < 22000)
        {
            detail.Status = BypassIoStatus.UnsupportedOsVersion;
            detail.SummaryMessage = "BypassIO requiere Windows 11.";
            return detail;
        }

        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "fsutil.exe",
                Arguments = $"bypassIo state /v \"{rootPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            proc.Start();
            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();
            proc.WaitForExit(3000);

            ParseFsutilOutput(output + "\n" + error, detail);
        }
        catch (Exception ex)
        {
            detail.Status = BypassIoStatus.Unknown;
            detail.SummaryMessage = $"Error al ejecutar fsutil: {ex.Message}";
        }

        return detail;
    }

    private void ParseFsutilOutput(string output, VolumeBypassIoDetail detail)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            detail.Status = BypassIoStatus.Unknown;
            detail.SummaryMessage = "No se recibió respuesta de fsutil.";
            return;
        }

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        bool volumeOk = false;
        bool storageOk = false;

        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            if (trimmed.Contains("is supported on this volume", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("BypassIo is supported", StringComparison.OrdinalIgnoreCase))
            {
                volumeOk = true;
            }

            if (trimmed.Contains("Storage driver is supported", StringComparison.OrdinalIgnoreCase))
            {
                storageOk = true;
            }

            if (trimmed.StartsWith("Driver:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Filter driver:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains(".sys", StringComparison.OrdinalIgnoreCase))
            {
                // Identify blocking drivers
                if (!trimmed.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    detail.BlockingDrivers.Add(trimmed);
                }
            }

            if (trimmed.StartsWith("Reason:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("not supported", StringComparison.OrdinalIgnoreCase))
            {
                detail.ReasonDetails.Add(trimmed);
            }
        }

        detail.VolumeSupported = volumeOk;
        detail.StorageDriverSupported = storageOk;

        if (volumeOk && storageOk && detail.BlockingDrivers.Count == 0)
        {
            detail.Status = BypassIoStatus.Supported;
            detail.SummaryMessage = "✓ BypassIO habilitado al 100%. Pila NVMe directa sin sobrecarga.";
        }
        else if (volumeOk || storageOk)
        {
            detail.Status = BypassIoStatus.PartiallySupported;
            string drivers = detail.BlockingDrivers.Count > 0 ? string.Join(", ", detail.BlockingDrivers) : "Filtros de sistema";
            detail.SummaryMessage = $"BypassIO parcial. Controladores/filtros activos: {drivers}.";
        }
        else
        {
            detail.Status = BypassIoStatus.NotSupported;
            string reasons = detail.ReasonDetails.Count > 0 ? string.Join("; ", detail.ReasonDetails) : "Incompatible con el tipo de disco o sistema de archivos.";
            detail.SummaryMessage = $"BypassIO no soportado: {reasons}";
        }
    }
}
