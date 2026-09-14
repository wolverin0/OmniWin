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
    public bool AutoEnforcePCores { get; set; } = true;
    public bool AutoSetTimer05ms { get; set; } = true;
    public bool AutoPurgeRam { get; set; } = true;
    public bool AutoHighPriority { get; set; } = true;
    public bool IsActive { get; set; } = false;
    public int ProcessId { get; set; } = 0;
}

public class GameProfilerService : IDisposable
{
    private static readonly Lazy<GameProfilerService> _instance = new(() => new GameProfilerService());
    public static GameProfilerService Instance => _instance.Value;

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
                    game.IsActive = false;
                    game.ProcessId = 0;
                    OnGameStatusChanged?.Invoke(game.DisplayName, false);
                }
            }
        }

        if (!anyGameRunning && _isTimerBoosted)
        {
            RevertTimerResolution();
        }
    }

    private void ApplyGameBoost(Process proc, GameProfileItem profile)
    {
        if (_boostedProcessIds.Contains(proc.Id)) return;

        try
        {
            // 1. High Priority
            if (profile.AutoHighPriority)
            {
                proc.PriorityClass = ProcessPriorityClass.High;
            }

            // 2. Enforce P-Cores (First 8 physical threads / cores on modern Intel/AMD CPUs)
            if (profile.AutoEnforcePCores)
            {
                int totalCores = Environment.ProcessorCount;
                if (totalCores >= 8)
                {
                    // Mask for first 8 or 16 threads (P-Cores)
                    long pCoreMask = (1L << Math.Min(16, totalCores)) - 1;
                    proc.ProcessorAffinity = (IntPtr)pCoreMask;
                }
            }

            // 3. Purge RAM before game starts
            if (profile.AutoPurgeRam)
            {
                var mem = new MemoryService();
                mem.PurgeMemory(true, true);
            }

            // 4. Force 0.5ms Timer Resolution
            if (profile.AutoSetTimer05ms && !_isTimerBoosted)
            {
                new PowerService().SetHighPrecisionTimer(true);
                _isTimerBoosted = true;
            }

            _boostedProcessIds.Add(proc.Id);
        }
        catch { }
    }

    private void RevertAllBoosts()
    {
        _boostedProcessIds.Clear();
        RevertTimerResolution();
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
