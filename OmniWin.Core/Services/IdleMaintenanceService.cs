using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class IdleMaintenanceConfig
{
    public int IdleThresholdMinutes { get; set; } = 10;
    public bool TrimSsdEnabled { get; set; } = true;
    public bool CleanTempEnabled { get; set; } = true;
    public bool PurgeRamIfHighEnabled { get; set; } = true;
    public int CooldownHours { get; set; } = 6;
}

public class IdleMaintenanceStatus
{
    public bool IsMonitoring { get; set; }
    public double CurrentIdleSeconds { get; set; }
    public double ThresholdSeconds { get; set; }
    public DateTime? LastRunTime { get; set; }
    public string LastRunSummary { get; set; } = "Sin ejecuciones recientes.";
    public bool IsMaintenanceRunning { get; set; }
}

public class IdleMaintenanceService
{
    public static IdleMaintenanceService Instance { get; } = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private Timer? _timer;
    private readonly IdleMaintenanceConfig _config = new();
    private DateTime? _lastRunTime;
    private string _lastRunSummary = "Sin ejecuciones recientes.";
    private bool _isMaintenanceRunning;
    private bool _isMonitoring;

    public event Action<string>? OnMaintenanceCompleted;

    public IdleMaintenanceConfig Config => _config;

    public double GetCurrentIdleSeconds()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref lii))
        {
            uint currentTick = (uint)Environment.TickCount;
            uint idleTicks = currentTick - lii.dwTime;
            return idleTicks / 1000.0;
        }
        return 0.0;
    }

    public IdleMaintenanceStatus GetStatus()
    {
        return new IdleMaintenanceStatus
        {
            IsMonitoring = _isMonitoring,
            CurrentIdleSeconds = Math.Round(GetCurrentIdleSeconds(), 1),
            ThresholdSeconds = _config.IdleThresholdMinutes * 60,
            LastRunTime = _lastRunTime,
            LastRunSummary = _lastRunSummary,
            IsMaintenanceRunning = _isMaintenanceRunning
        };
    }

    public void Start()
    {
        if (_isMonitoring) return;
        _isMonitoring = true;
        _timer = new Timer(CheckIdleState, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
    }

    public void Stop()
    {
        _isMonitoring = false;
        _timer?.Dispose();
        _timer = null;
    }

    private void CheckIdleState(object? state)
    {
        if (!_isMonitoring || _isMaintenanceRunning) return;

        double idleSeconds = GetCurrentIdleSeconds();
        double thresholdSeconds = _config.IdleThresholdMinutes * 60.0;

        if (idleSeconds >= thresholdSeconds)
        {
            if (_lastRunTime.HasValue && (DateTime.UtcNow - _lastRunTime.Value).TotalHours < _config.CooldownHours)
            {
                return;
            }

            _ = Task.Run(async () => await ExecuteMaintenanceAsync(isAutomated: true));
        }
    }

    public async Task<string> TriggerMaintenanceNowAsync()
    {
        return await ExecuteMaintenanceAsync(isAutomated: false);
    }

    private async Task<string> ExecuteMaintenanceAsync(bool isAutomated)
    {
        if (_isMaintenanceRunning) return "El mantenimiento ya se encuentra en ejecución.";
        _isMaintenanceRunning = true;
        var sw = Stopwatch.StartNew();

        int tasksCompleted = 0;
        var logs = new System.Text.StringBuilder();

        try
        {
            logs.AppendLine($"[IDLE_MAINTENANCE] Inicio {(isAutomated ? "Automático (Inactividad detectada)" : "Manual")}.");

            // 1. Re-Trim SSD/NVMe
            if (_config.TrimSsdEnabled)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        var psi = new ProcessStartInfo("powershell.exe",
                            "-NoProfile -Command \"Get-Volume | Where-Object { $_.DriveType -eq 'Fixed' -and $_.DriveLetter } | ForEach-Object { Optimize-Volume -DriveLetter $_.DriveLetter -ReTrim -ErrorAction SilentlyContinue }\"")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var proc = Process.Start(psi);
                        proc?.WaitForExit(15000);
                    });
                    tasksCompleted++;
                    logs.AppendLine("• Re-Trim de SSDs y unidades fijas completado.");
                }
                catch (Exception ex)
                {
                    logs.AppendLine($"• Fallo en Re-Trim: {ex.Message}");
                }
            }

            // 2. RAM Purge if high load (> 80%)
            if (_config.PurgeRamIfHighEnabled)
            {
                try
                {
                    var memService = new MemoryService();
                    var stats = memService.GetMemoryStats();
                    if (stats.TotalPhysicalBytes > 0 && stats.UsagePercentage > 80.0)
                    {
                        var purgeRes = memService.PurgeMemory(purgeStandby: true, purgeWorkingSets: false);
                        if (purgeRes.Success)
                        {
                            tasksCompleted++;
                            logs.AppendLine("• Memoria RAM alta (>80%): Standby List purgada con éxito.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logs.AppendLine($"• Fallo en purga de RAM: {ex.Message}");
                }
            }

            // 3. Clean temporary files
            if (_config.CleanTempEnabled)
            {
                try
                {
                    int cleanedFiles = CleanTempFolder(Path.GetTempPath());
                    string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                    if (Directory.Exists(winTemp)) cleanedFiles += CleanTempFolder(winTemp);

                    tasksCompleted++;
                    logs.AppendLine($"• Archivos temporales obsoletos purgados ({cleanedFiles} archivos eliminados).");
                }
                catch (Exception ex)
                {
                    logs.AppendLine($"• Fallo en limpieza de temporales: {ex.Message}");
                }
            }

            sw.Stop();
            _lastRunTime = DateTime.UtcNow;
            _lastRunSummary = $"{tasksCompleted} tareas completadas en {sw.ElapsedMilliseconds} ms. {DateTime.Now:HH:mm:ss}";
            logs.AppendLine($"[IDLE_MAINTENANCE] Completado en {sw.ElapsedMilliseconds} ms.");

            string finalLog = logs.ToString();
            OnMaintenanceCompleted?.Invoke(finalLog);
            return finalLog;
        }
        finally
        {
            _isMaintenanceRunning = false;
        }
    }

    private static int CleanTempFolder(string tempPath)
    {
        if (!Directory.Exists(tempPath)) return 0;
        int count = 0;
        var now = DateTime.UtcNow;

        try
        {
            foreach (var f in Directory.GetFiles(tempPath))
            {
                try
                {
                    var fi = new FileInfo(f);
                    // Only delete files older than 24 hours to prevent deleting in-use locks
                    if ((now - fi.LastWriteTimeUtc).TotalHours >= 24)
                    {
                        fi.Delete();
                        count++;
                    }
                }
                catch { }
            }
        }
        catch { }

        return count;
    }
}
