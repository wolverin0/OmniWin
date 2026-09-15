using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

public record ThrottledProcessIdentity(int Pid, DateTime StartTime, ProcessPriorityClass OriginalPriority);

public class EcoQoSService
{
    private static readonly Lazy<EcoQoSService> _instance = new(() => new EcoQoSService());
    public static EcoQoSService Instance => _instance.Value;

    private const int PROCESS_SET_INFORMATION = 0x0200;
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ProcessPowerThrottling = 4;

    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess,
        int ProcessInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
        uint ProcessInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private readonly Dictionary<int, ThrottledProcessIdentity> _throttledProcesses = new();
    private readonly object _lock = new();

    // Known background workers safe to throttle to EcoQoS during competitive gaming
    private static readonly HashSet<string> DefaultCandidateExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera",
        "steamwebhelper", "epicgameslauncher", "galaxyclient", "originwebhelperservice", "eadesktop",
        "discord", "slack", "teams", "spotify", "dropbox", "onedrive", "searchindexer"
    };

    // Protected processes that MUST NEVER be throttled
    private static readonly HashSet<string> ProtectedExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audiodg", "dwm", "explorer", "csrss", "lsass", "services", "smss", "svchost", "obs64"
    };

    public bool SetProcessEcoQoS(int pid, bool enable)
    {
        Process proc;
        DateTime startTime;
        try
        {
            proc = Process.GetProcessById(pid);
            startTime = proc.StartTime;
        }
        catch
        {
            return false;
        }

        using (proc)
        {
            lock (_lock)
            {
                if (enable)
                {
                    // Check if already throttled
                    if (_throttledProcesses.TryGetValue(pid, out var existing) && existing.StartTime == startTime)
                    {
                        return true;
                    }

                    ProcessPriorityClass originalPriority;
                    try
                    {
                        originalPriority = proc.PriorityClass;
                    }
                    catch
                    {
                        originalPriority = ProcessPriorityClass.Normal;
                    }

                    // 1. Open process handle for SetProcessInformation
                    IntPtr hProc = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (hProc == IntPtr.Zero) return false;

                    try
                    {
                        var state = new PROCESS_POWER_THROTTLING_STATE
                        {
                            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                            StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED
                        };

                        uint size = (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE));
                        bool ecoOk = SetProcessInformation(hProc, ProcessPowerThrottling, ref state, size);
                        if (!ecoOk)
                        {
                            return false;
                        }

                        // 2. Set PriorityClass to Idle with atomic rollback on failure
                        try
                        {
                            proc.PriorityClass = ProcessPriorityClass.Idle;
                        }
                        catch
                        {
                            // Roll back EcoQoS if priority modification failed
                            state.StateMask = 0;
                            SetProcessInformation(hProc, ProcessPowerThrottling, ref state, size);
                            return false;
                        }

                        _throttledProcesses[pid] = new ThrottledProcessIdentity(pid, startTime, originalPriority);
                        return true;
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
                else
                {
                    // Disabling / Reverting
                    if (!_throttledProcesses.TryGetValue(pid, out var identity))
                    {
                        return false;
                    }

                    // Verify PID hasn't been recycled by Windows
                    if (startTime != identity.StartTime)
                    {
                        _throttledProcesses.Remove(pid);
                        return false;
                    }

                    IntPtr hProc = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (hProc == IntPtr.Zero) return false;

                    try
                    {
                        var state = new PROCESS_POWER_THROTTLING_STATE
                        {
                            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                            StateMask = 0
                        };

                        uint size = (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE));
                        bool ecoOk = SetProcessInformation(hProc, ProcessPowerThrottling, ref state, size);

                        try
                        {
                            proc.PriorityClass = identity.OriginalPriority;
                        }
                        catch { }

                        _throttledProcesses.Remove(pid);
                        return ecoOk;
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
            }
        }
    }

    public int ApplyEcoQoSToBackgroundApps(IEnumerable<int>? excludePids = null)
    {
        var excludeSet = new HashSet<int>(excludePids ?? Enumerable.Empty<int>());
        int currentPid = Environment.ProcessId;
        excludeSet.Add(currentPid);

        int throttledCount = 0;
        lock (_lock)
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    if (excludeSet.Contains(proc.Id) || _throttledProcesses.ContainsKey(proc.Id))
                        continue;

                    string name = proc.ProcessName;
                    if (ProtectedExes.Contains(name))
                        continue;

                    if (DefaultCandidateExes.Contains(name))
                    {
                        if (SetProcessEcoQoS(proc.Id, true))
                        {
                            throttledCount++;
                        }
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }

        return throttledCount;
    }

    public int RevertAllEcoQoS()
    {
        int revertedCount = 0;
        lock (_lock)
        {
            foreach (var identity in _throttledProcesses.Values.ToList())
            {
                try
                {
                    if (SetProcessEcoQoS(identity.Pid, false))
                    {
                        revertedCount++;
                    }
                }
                catch { }
            }
            _throttledProcesses.Clear();
        }
        return revertedCount;
    }

    public IReadOnlyCollection<int> GetThrottledPids()
    {
        lock (_lock)
        {
            return _throttledProcesses.Keys.ToList();
        }
    }
}
