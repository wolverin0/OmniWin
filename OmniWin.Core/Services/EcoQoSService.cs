using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

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

    private readonly HashSet<int> _throttledPids = new();
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
        IntPtr hProc = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == IntPtr.Zero) return false;

        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = enable ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
            };

            uint size = (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE));
            return SetProcessInformation(hProc, ProcessPowerThrottling, ref state, size);
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseHandle(hProc);
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
                    if (excludeSet.Contains(proc.Id) || _throttledPids.Contains(proc.Id))
                        continue;

                    string name = proc.ProcessName;
                    if (ProtectedExes.Contains(name))
                        continue;

                    if (DefaultCandidateExes.Contains(name))
                    {
                        if (SetProcessEcoQoS(proc.Id, true))
                        {
                            _throttledPids.Add(proc.Id);
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
            foreach (int pid in _throttledPids.ToList())
            {
                try
                {
                    if (SetProcessEcoQoS(pid, false))
                    {
                        revertedCount++;
                    }
                }
                catch { }
            }
            _throttledPids.Clear();
        }
        return revertedCount;
    }

    public IReadOnlyCollection<int> GetThrottledPids()
    {
        lock (_lock)
        {
            return _throttledPids.ToList();
        }
    }
}
