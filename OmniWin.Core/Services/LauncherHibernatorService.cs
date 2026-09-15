using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

public class HibernatedProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // "Launcher", "Comms", "Browser", "Media"
    public ProcessPriorityClass OriginalPriority { get; set; } = ProcessPriorityClass.Normal;
    public double InitialWorkingSetMb { get; set; }
    public double TrimmedWorkingSetMb { get; set; }
    public DateTime HibernatedAt { get; set; } = DateTime.UtcNow;
}

public class LauncherHibernationStatus
{
    public bool IsHibernating { get; set; }
    public int HibernatedProcessCount { get; set; }
    public double TotalMemoryFreedMb { get; set; }
    public List<HibernatedProcessInfo> Processes { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class LauncherHibernatorService
{
    private static readonly Lazy<LauncherHibernatorService> _instance = new(() => new LauncherHibernatorService());
    public static LauncherHibernatorService Instance => _instance.Value;

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    private readonly ConcurrentDictionary<int, HibernatedProcessInfo> _hibernated = new();
    private readonly object _lock = new();

    // Known app groupings
    private static readonly Dictionary<string, string> TargetCatalog = new(StringComparer.OrdinalIgnoreCase)
    {
        // Game Launchers & Storefront WebViews
        { "steamwebhelper", "Launcher" },
        { "epicgameslauncher", "Launcher" },
        { "battle.net", "Launcher" },
        { "eadesktop", "Launcher" },
        { "galaxyclient", "Launcher" },
        { "riotclientservices", "Launcher" },
        { "ubisoftconnect", "Launcher" },

        // Voice & Communication
        { "discord", "Comms" },
        { "slack", "Comms" },
        { "teams", "Comms" },

        // Browsers
        { "chrome", "Browser" },
        { "msedge", "Browser" },
        { "firefox", "Browser" },
        { "brave", "Browser" },
        { "opera", "Browser" },
        { "vivaldi", "Browser" },

        // Media & Music
        { "spotify", "Media" }
    };

    public bool HibernateLaunchers { get; set; } = true;
    public bool HibernateComms { get; set; } = true;
    public bool HibernateBrowsers { get; set; } = true;
    public bool HibernateMedia { get; set; } = true;
    public bool TrimWorkingSetsOnHibernation { get; set; } = false;

    public bool IsHibernating => !_hibernated.IsEmpty;

    public LauncherHibernationStatus HibernateBackgroundProcesses(IEnumerable<int>? excludePids = null)
    {
        var excludeSet = new HashSet<int>(excludePids ?? Enumerable.Empty<int>());
        excludeSet.Add(Environment.ProcessId);

        var runningProcesses = Process.GetProcesses();
        double initialTotalMb = 0;
        double trimmedTotalMb = 0;

        lock (_lock)
        {
            foreach (var proc in runningProcesses)
            {
                try
                {
                    if (excludeSet.Contains(proc.Id)) continue;
                    if (_hibernated.ContainsKey(proc.Id)) continue;

                    string name = proc.ProcessName.ToLowerInvariant();
                    if (!TargetCatalog.TryGetValue(name, out string? category)) continue;

                    // Check user preferences per category
                    if (category == "Launcher" && !HibernateLaunchers) continue;
                    if (category == "Comms" && !HibernateComms) continue;
                    if (category == "Browser" && !HibernateBrowsers) continue;
                    if (category == "Media" && !HibernateMedia) continue;

                    double wsBefore = proc.WorkingSet64 / (1024.0 * 1024.0);
                    initialTotalMb += wsBefore;

                    var info = new HibernatedProcessInfo
                    {
                        ProcessId = proc.Id,
                        ProcessName = proc.ProcessName,
                        Category = category,
                        InitialWorkingSetMb = wsBefore,
                        HibernatedAt = DateTime.UtcNow
                    };

                    try
                    {
                        info.OriginalPriority = proc.PriorityClass;
                    }
                    catch
                    {
                        info.OriginalPriority = ProcessPriorityClass.Normal;
                    }

                    // 1. Force EcoQoS (Efficiency Cores & low execution speed, handles Idle priority modulation atomically)
                    EcoQoSService.Instance.SetProcessEcoQoS(proc.Id, true);

                    // 2. Trim inactive memory pages if requested (flushes bloated Chromium heap to standby)
                    if (TrimWorkingSetsOnHibernation)
                    {
                        try
                        {
                            EmptyWorkingSet(proc.Handle);
                            proc.Refresh();
                            double wsAfter = proc.WorkingSet64 / (1024.0 * 1024.0);
                            info.TrimmedWorkingSetMb = wsAfter;
                            trimmedTotalMb += wsAfter;
                        }
                        catch
                        {
                            info.TrimmedWorkingSetMb = wsBefore;
                            trimmedTotalMb += wsBefore;
                        }
                    }
                    else
                    {
                        info.TrimmedWorkingSetMb = wsBefore;
                        trimmedTotalMb += wsBefore;
                    }

                    _hibernated[proc.Id] = info;
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }

        double freedMb = Math.Max(0, initialTotalMb - trimmedTotalMb);
        return new LauncherHibernationStatus
        {
            IsHibernating = !_hibernated.IsEmpty,
            HibernatedProcessCount = _hibernated.Count,
            TotalMemoryFreedMb = freedMb,
            Processes = _hibernated.Values.OrderBy(p => p.Category).ThenBy(p => p.ProcessName).ToList(),
            Summary = _hibernated.IsEmpty
                ? "No se encontraron procesos de launchers o navegadores secundarios en ejecución."
                : $"Hibernados {_hibernated.Count} procesos secundarios. Se liberaron ~{freedMb:F0} MB de RAM física para el juego y se asignó EcoQoS."
        };
    }

    public int WakeAllHibernatedProcesses()
    {
        int wokenCount = 0;
        lock (_lock)
        {
            foreach (var kvp in _hibernated)
            {
                int pid = kvp.Key;
                var info = kvp.Value;

                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        // 1. Remove EcoQoS
                        EcoQoSService.Instance.SetProcessEcoQoS(pid, false);

                        // 2. Restore Priority
                        try
                        {
                            proc.PriorityClass = info.OriginalPriority;
                        }
                        catch { }

                        wokenCount++;
                    }
                }
                catch { }
            }

            _hibernated.Clear();
        }

        return wokenCount;
    }

    public LauncherHibernationStatus GetStatus()
    {
        lock (_lock)
        {
            double initialTotal = _hibernated.Values.Sum(p => p.InitialWorkingSetMb);
            double trimmedTotal = _hibernated.Values.Sum(p => p.TrimmedWorkingSetMb);
            double freed = Math.Max(0, initialTotal - trimmedTotal);

            return new LauncherHibernationStatus
            {
                IsHibernating = !_hibernated.IsEmpty,
                HibernatedProcessCount = _hibernated.Count,
                TotalMemoryFreedMb = freed,
                Processes = _hibernated.Values.OrderBy(p => p.Category).ThenBy(p => p.ProcessName).ToList(),
                Summary = _hibernated.IsEmpty
                    ? "El hibernador de launchers está inactivo (sin procesos restringidos)."
                    : $"{_hibernated.Count} procesos hibernados (~{freed:F0} MB recuperados)."
            };
        }
    }
}
