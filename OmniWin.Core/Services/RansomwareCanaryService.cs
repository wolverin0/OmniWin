using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniWin.Core.Services;

public record CanaryBreachAlert(
    string CanaryFilePath,
    int? OffendingPid,
    string OffendingProcessName,
    string OffendingProcessPath,
    DateTime Timestamp,
    bool WasProcessSuspended,
    string ActionTaken
);

public record RansomwareCanaryStatus(
    bool IsActive,
    List<string> MonitoredFolders,
    List<string> ActiveCanaries,
    int BreachesDetected
);

public class RansomwareCanaryService : IDisposable
{
    private static readonly Lazy<RansomwareCanaryService> _instance = new(() => new RansomwareCanaryService());
    public static RansomwareCanaryService Instance => _instance.Value;

    public event Action<CanaryBreachAlert>? CanaryBreached;

    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly List<string> _canaryPaths = new();
    private readonly List<CanaryBreachAlert> _alerts = new();
    private readonly object _lock = new();
    private bool _isActive = false;

    private const int PROCESS_SUSPEND_RESUME = 0x0800;
    private const int PROCESS_QUERY_INFORMATION = 0x0400;

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public RansomwareCanaryStatus GetStatus()
    {
        lock (_lock)
        {
            var folders = new List<string>();
            foreach (var w in _watchers)
            {
                folders.Add(w.Path);
            }

            return new RansomwareCanaryStatus(
                IsActive: _isActive,
                MonitoredFolders: folders,
                ActiveCanaries: new List<string>(_canaryPaths),
                BreachesDetected: _alerts.Count
            );
        }
    }

    public List<CanaryBreachAlert> GetRecentAlerts()
    {
        lock (_lock)
        {
            return new List<CanaryBreachAlert>(_alerts);
        }
    }

    /// <summary>
    /// Despliega archivos trampa (canarios) en las carpetas principales del usuario e inicia el monitoreo en tiempo real.
    /// </summary>
    public void StartShield()
    {
        lock (_lock)
        {
            if (_isActive) return;

            StopShieldInternal();

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var targetFolders = new List<string>
            {
                Path.Combine(userProfile, "Documents"),
                Path.Combine(userProfile, "Desktop"),
                Path.Combine(userProfile, "Pictures")
            };

            foreach (var folder in targetFolders)
            {
                if (!Directory.Exists(folder)) continue;

                // Crear 2 canarios por carpeta (uno con formato docx/zip, otro con pdf)
                string canary1 = Path.Combine(folder, "!_omni_canary_backup.docx");
                string canary2 = Path.Combine(folder, "!_omni_canary_statement.pdf");

                DeployCanaryFile(canary1, isDocx: true);
                DeployCanaryFile(canary2, isDocx: false);

                _canaryPaths.Add(canary1);
                _canaryPaths.Add(canary2);

                try
                {
                    var watcher = new FileSystemWatcher(folder, "!_omni_canary_*.*")
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };

                    watcher.Changed += OnCanaryTampered;
                    watcher.Deleted += OnCanaryTampered;
                    watcher.Renamed += OnCanaryRenamed;

                    _watchers.Add(watcher);
                }
                catch { }
            }

            _isActive = true;
        }
    }

    /// <summary>
    /// Detiene el monitoreo y remueve los archivos trampa desplegados.
    /// </summary>
    public void StopShield()
    {
        lock (_lock)
        {
            StopShieldInternal();
        }
    }

    private void StopShieldInternal()
    {
        foreach (var w in _watchers)
        {
            try
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            catch { }
        }
        _watchers.Clear();

        foreach (var canary in _canaryPaths)
        {
            try
            {
                if (File.Exists(canary)) File.Delete(canary);
            }
            catch { }
        }
        _canaryPaths.Clear();
        _isActive = false;
    }

    private void OnCanaryTampered(object sender, FileSystemEventArgs e)
    {
        HandleTampering(e.FullPath, e.ChangeType.ToString());
    }

    private void OnCanaryRenamed(object sender, RenamedEventArgs e)
    {
        HandleTampering(e.OldFullPath, $"Renombrado a {e.Name}");
    }

    private void HandleTampering(string filePath, string mutationType)
    {
        int myPid = Environment.ProcessId;
        int? offendingPid = null;
        string procName = "Desconocido";
        string procPath = "No disponible";
        bool suspended = false;

        // Intentar identificar el proceso que abrió el archivo usando FileLockService
        try
        {
            var lockService = new FileLockService();
            var lockers = lockService.GetLockingProcesses(filePath);
            var culprit = lockers.FirstOrDefault(p => p.ProcessId != myPid);

            if (culprit != null)
            {
                offendingPid = culprit.ProcessId;
                procName = culprit.ProcessName;

                try
                {
                    using var p = Process.GetProcessById(culprit.ProcessId);
                    procPath = p.MainModule?.FileName ?? "N/D";
                }
                catch { }

                // ¡CONGELAR PROCESO SOSPECHOSO INMEDIATAMENTE!
                suspended = SuspendProcess(culprit.ProcessId);
            }
        }
        catch { }

        string action = suspended
            ? $"¡RANSOMWARE CONGELADO! El proceso '{procName}' (PID {offendingPid}) intentó alterar el canario y fue suspendido con NtSuspendProcess."
            : $"Alerta de seguridad: El canario '{Path.GetFileName(filePath)}' fue alterado ({mutationType}).";

        var alert = new CanaryBreachAlert(
            CanaryFilePath: filePath,
            OffendingPid: offendingPid,
            OffendingProcessName: procName,
            OffendingProcessPath: procPath,
            Timestamp: DateTime.Now,
            WasProcessSuspended: suspended,
            ActionTaken: action
        );

        lock (_lock)
        {
            _alerts.Insert(0, alert);
            if (_alerts.Count > 100) _alerts.RemoveAt(_alerts.Count - 1);
        }

        CanaryBreached?.Invoke(alert);
    }

    public static bool SuspendProcess(int pid)
    {
        IntPtr hProcess = OpenProcess(PROCESS_SUSPEND_RESUME | PROCESS_QUERY_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) return false;

        try
        {
            int status = NtSuspendProcess(hProcess);
            return status == 0;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    public static bool ResumeProcess(int pid)
    {
        IntPtr hProcess = OpenProcess(PROCESS_SUSPEND_RESUME | PROCESS_QUERY_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) return false;

        try
        {
            int status = NtResumeProcess(hProcess);
            return status == 0;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    private static void DeployCanaryFile(string path, bool isDocx)
    {
        try
        {
            if (File.Exists(path)) return;

            if (isDocx)
            {
                // Cabecera ZIP 'PK\x03\x04' con token canary
                byte[] zipHeader = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00];
                byte[] token = Encoding.UTF8.GetBytes("[OMNIWIN_CANARY_TRAP_INTEGRITY_TOKEN_" + Guid.NewGuid() + "]");
                using var fs = File.Create(path);
                fs.Write(zipHeader);
                fs.Write(token);
            }
            else
            {
                // Cabecera PDF '%PDF-1.7' con token
                byte[] pdfHeader = Encoding.UTF8.GetBytes("%PDF-1.7\n%OmniWin Canary Trap Security Beacon\n" + Guid.NewGuid() + "\n%%EOF");
                File.WriteAllBytes(path, pdfHeader);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        StopShield();
    }
}
