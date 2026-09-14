using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

public class DynamicThermalProfileService
{
    public static DynamicThermalProfileService Instance { get; } = new();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static readonly HashSet<string> GameProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs2", "csgo", "valorant", "cyberpunk2077", "r5apex", "fortniteclient-win64-shipping",
        "leagueclientux", "dota2", "gta5", "gadv", "overwatch", "destiny2", "rainbowsix",
        "warzone", "modernwarfare", "cod", "eldenring", "starfield", "forzahorizon5",
        "witcher3", "minecraft.windows", "javaw", "helldivers2", "pubg", "blackops"
    };

    public bool AutoSwitchEnabled { get; set; } = true;
    public string CurrentActiveProfileId { get; private set; } = "balanced";
    public string DetectedForegroundProcess { get; private set; } = string.Empty;
    public bool IsGameDetected { get; private set; } = false;

    public event Action<string, string>? OnProfileAutoSwitched;

    public void CheckForegroundProcessAndApplyProfile()
    {
        if (!AutoSwitchEnabled) return;

        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            string procName = string.Empty;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                procName = proc.ProcessName;
            }
            catch { return; }

            DetectedForegroundProcess = procName;
            bool isGame = GameProcessNames.Contains(procName);
            IsGameDetected = isGame;

            string targetProfileId = isGame ? "gamer_performance" : "balanced";

            if (targetProfileId != CurrentActiveProfileId)
            {
                CurrentActiveProfileId = targetProfileId;
                FanCurveService.Instance.SetActiveProfile(targetProfileId);
                OnProfileAutoSwitched?.Invoke(targetProfileId, procName);
            }
        }
        catch { }
    }

    public void AddCustomGameProcess(string processName)
    {
        string clean = Path.GetFileNameWithoutExtension(processName).Trim();
        if (!string.IsNullOrWhiteSpace(clean))
        {
            GameProcessNames.Add(clean);
        }
    }
}
