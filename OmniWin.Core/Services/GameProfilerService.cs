using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class GameProfileItem
{
    public string ExecutableName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool AutoEnforcePCores { get; set; } = false; // Scheduler-managed by default; empirical benchmark required
    public bool AutoSetTimer05ms { get; set; } = true;
    public bool AutoPurgeRam { get; set; } = false; // Disabled by default: prevent launch working set flush & hard faults
    public bool AutoHighPriority { get; set; } = true;
    public bool AutoEcoQoSBackground { get; set; } = true; // EcoQoS for secondary apps (Chrome, Discord, Steam helpers)
    public bool AutoHibernateLaunchers { get; set; } = true; // Suspend & trim bloated launchers/browsers during gaming
    public bool IsActive { get; set; } = false;
    public int ProcessId { get; set; } = 0;
}

public class GameProfilerService : IDisposable
{
    private static readonly Lazy<GameProfilerService> _instance = new(() => new GameProfilerService());
    public static GameProfilerService Instance => _instance.Value;

    private class ProcessOriginalState
    {
        public DateTime StartTime { get; set; }
        public ProcessPriorityClass OriginalPriority { get; set; }
        public IntPtr OriginalAffinity { get; set; }
    }

    private readonly Dictionary<int, ProcessOriginalState> _originalProcessStates = new();
    private readonly List<GameProfileItem> _monitoredGames = new()
    {
        new GameProfileItem { ExecutableName = "cs2.exe", DisplayName = "Counter-Strike 2" },
        new GameProfileItem { ExecutableName = "valorant-win64-shipping.exe", DisplayName = "Valorant" },
        new GameProfileItem { ExecutableName = "r5apex.exe", DisplayName = "Apex Legends" },
        new GameProfileItem { ExecutableName = "fortniteclient-win64-shipping.exe", DisplayName = "Fortnite" },
        new GameProfileItem { ExecutableName = "cod.exe", DisplayName = "Call of Duty / Warzone" },
        new GameProfileItem { ExecutableName = "cyberpunk2077.exe", DisplayName = "Cyberpunk 2077" },
        new GameProfileItem { ExecutableName = "gta5.exe", DisplayName = "Grand Theft Auto V" },
        new GameProfileItem { ExecutableName = "dota2.exe", DisplayName = "Dota 2" },
        new GameProfileItem { ExecutableName = "league of legends.exe", DisplayName = "League of Legends" },
        new GameProfileItem { ExecutableName = "blender.exe", DisplayName = "Blender (Workstation Render)" },
        new GameProfileItem { ExecutableName = "devenv.exe", DisplayName = "Visual Studio (Build Pipeline)" }
    };

    private CancellationTokenSource? _cts;
    private readonly HashSet<int> _boostedProcessIds = new();
    private bool _isTimerBoosted = false;

    public event Action<string, bool>? OnGameStatusChanged;

    public List<GameProfileItem> GetMonitoredGames() => _monitoredGames.ToList();

    public void AddMonitoredGame(string exeName, string displayName)
    {
        string cleanExe = Path.GetFileName(exeName).ToLowerInvariant();
        if (!_monitoredGames.Any(g => string.Equals(g.ExecutableName, cleanExe, StringComparison.OrdinalIgnoreCase)))
        {
            _monitoredGames.Add(new GameProfileItem
            {
                ExecutableName = cleanExe,
                DisplayName = displayName
            });
        }
    }

    public void RemoveMonitoredGame(string exeName)
    {
        _monitoredGames.RemoveAll(g => string.Equals(g.ExecutableName, exeName, StringComparison.OrdinalIgnoreCase));
    }

    public void StartMonitoring()
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        Task.Run(() => PollingLoopAsync(_cts.Token));
    }

    public void Start() => StartMonitoring();

    public void StopMonitoring()
    {
        _cts?.Cancel();
        _cts = null;
        RevertAllBoosts();
    }

    public void Stop() => StopMonitoring();

    public bool IsRunning => _cts != null;

    private async Task PollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                CheckRunningProcesses();
                await Task.Delay(2000, ct);
            }
            catch (TaskCanceledException) { break; }
            catch { }
        }
    }

    public void CheckRunningProcesses()
    {
        var runningProcesses = Process.GetProcesses();
        var runningLookup = new Dictionary<string, Process>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in runningProcesses)
        {
            try
            {
                string procName = p.ProcessName.ToLowerInvariant();
                string exeName = procName.EndsWith(".exe") ? procName : procName + ".exe";
                if (!runningLookup.ContainsKey(exeName))
                {
                    runningLookup[exeName] = p;
                }
            }
            catch { }
        }

        bool anyGameRunning = false;

        foreach (var game in _monitoredGames)
        {
            if (runningLookup.TryGetValue(game.ExecutableName, out var proc))
            {
                anyGameRunning = true;
                if (!game.IsActive || game.ProcessId != proc.Id)
                {
                    game.IsActive = true;
                    game.ProcessId = proc.Id;
                    ApplyGameBoost(proc, game);
                    OnGameStatusChanged?.Invoke(game.DisplayName, true);
                }
            }
            else
            {
                if (game.IsActive)
                {
                    int oldPid = game.ProcessId;
                    game.IsActive = false;
                    game.ProcessId = 0;
                    RevertProcessBoost(oldPid);
                    OnGameStatusChanged?.Invoke(game.DisplayName, false);
                }
            }
        }

        if (!anyGameRunning)
        {
            if (_isTimerBoosted)
            {
                RevertTimerResolution();
            }
            EcoQoSService.Instance.RevertAllEcoQoS();
            LauncherHibernatorService.Instance.WakeAllHibernatedProcesses();
        }
    }

    private void ApplyGameBoost(Process proc, GameProfileItem profile)
    {
        if (_boostedProcessIds.Contains(proc.Id)) return;

        try
        {
            // Capture original state before making any modifications
            try
            {
                if (!_originalProcessStates.ContainsKey(proc.Id))
                {
                    _originalProcessStates[proc.Id] = new ProcessOriginalState
                    {
                        StartTime = proc.StartTime,
                        OriginalPriority = proc.PriorityClass,
                        OriginalAffinity = proc.ProcessorAffinity
                    };
                }
            }
            catch { }

            // 1. High Priority
            if (profile.AutoHighPriority)
            {
                proc.PriorityClass = ProcessPriorityClass.High;
            }

            // 2. Enforce P-Cores only if explicitly requested (Default is scheduler-managed)
            if (profile.AutoEnforcePCores)
            {
                int totalCores = Environment.ProcessorCount;
                if (totalCores >= 8)
                {
                    // If hybrid CPU topology provides specific P-core mask, use it; otherwise leave to Windows Thread Director
                    long pCoreMask = CpuTopologyService.Instance.GetPerformanceCoreMask();
                    if (pCoreMask > 0)
                    {
                        proc.ProcessorAffinity = (IntPtr)pCoreMask;
                    }
                }
            }

            // 3. Purge RAM before game starts (Only if explicitly enabled by user)
            if (profile.AutoPurgeRam)
            {
                var mem = new MemoryService();
                mem.PurgeMemory(purgeStandby: true, purgeWorkingSets: false);
            }

            // 4. Force 0.5ms Timer Resolution
            if (profile.AutoSetTimer05ms && !_isTimerBoosted)
            {
                new PowerService().SetHighPrecisionTimer(true);
                _isTimerBoosted = true;
            }

            // 5. Apply EcoQoS to secondary background apps
            if (profile.AutoEcoQoSBackground)
            {
                EcoQoSService.Instance.ApplyEcoQoSToBackgroundApps(new[] { proc.Id });
            }

            // 6. Intelligent Launcher & WebView Hibernation
            if (profile.AutoHibernateLaunchers)
            {
                LauncherHibernatorService.Instance.HibernateBackgroundProcesses(new[] { proc.Id });
            }

            _boostedProcessIds.Add(proc.Id);
        }
        catch { }
    }

    private void RevertProcessBoost(int pid)
    {
        if (_originalProcessStates.TryGetValue(pid, out var orig))
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                // Verify PID hasn't been recycled by checking StartTime
                if (!proc.HasExited && proc.StartTime == orig.StartTime)
                {
                    proc.PriorityClass = orig.OriginalPriority;
                    proc.ProcessorAffinity = orig.OriginalAffinity;
                }
            }
            catch { }
            finally
            {
                _originalProcessStates.Remove(pid);
            }
        }
        _boostedProcessIds.Remove(pid);
    }

    private void RevertAllBoosts()
    {
        foreach (var pid in _originalProcessStates.Keys.ToList())
        {
            RevertProcessBoost(pid);
        }
        _boostedProcessIds.Clear();
        _originalProcessStates.Clear();
        RevertTimerResolution();
        EcoQoSService.Instance.RevertAllEcoQoS();
        LauncherHibernatorService.Instance.WakeAllHibernatedProcesses();
    }

    private void RevertTimerResolution()
    {
        if (_isTimerBoosted)
        {
            new PowerService().SetHighPrecisionTimer(false);
            _isTimerBoosted = false;
        }
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
