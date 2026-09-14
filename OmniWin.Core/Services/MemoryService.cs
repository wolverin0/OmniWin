using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace OmniWin.Core.Services;

public class MemoryStats
{
    public ulong TotalPhysicalBytes { get; set; }
    public ulong AvailablePhysicalBytes { get; set; }
    public ulong UsedPhysicalBytes => TotalPhysicalBytes > AvailablePhysicalBytes ? TotalPhysicalBytes - AvailablePhysicalBytes : 0;
    public double UsagePercentage => TotalPhysicalBytes > 0 ? (double)UsedPhysicalBytes / TotalPhysicalBytes * 100.0 : 0.0;
    public ulong TotalPageFileBytes { get; set; }
    public ulong AvailablePageFileBytes { get; set; }
    public uint MemoryLoad { get; set; }
}

public class MemoryPurgeResult
{
    public bool Success { get; set; }
    public ulong BytesFreed { get; set; }
    public ulong MemoryBeforeUsed { get; set; }
    public ulong MemoryAfterUsed { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class MemoryService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public static MEMORYSTATUSEX Create()
        {
            var result = new MEMORYSTATUSEX();
            result.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            return result;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    [DllImport("ntdll.dll")]
    private static extern uint NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryEmptyWorkingSets = 2;

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool EnablePrivilege(string privilegeName)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr tokenHandle))
            return false;

        try
        {
            if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                return false;

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED
            };

            return AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(tokenHandle);
        }
    }

    public MemoryStats GetMemoryStats()
    {
        var memStatus = MEMORYSTATUSEX.Create();
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            return new MemoryStats
            {
                TotalPhysicalBytes = memStatus.ullTotalPhys,
                AvailablePhysicalBytes = memStatus.ullAvailPhys,
                TotalPageFileBytes = memStatus.ullTotalPageFile,
                AvailablePageFileBytes = memStatus.ullAvailPageFile,
                MemoryLoad = memStatus.dwMemoryLoad
            };
        }

        return new MemoryStats();
    }

    public MemoryPurgeResult PurgeMemory(bool purgeStandby = true, bool purgeWorkingSets = true)
    {
        var before = GetMemoryStats();
        int emptiedProcesses = 0;
        bool standbyPurged = false;

        if (IsAdmin())
        {
            EnablePrivilege("SeIncreaseQuotaPrivilege");
            EnablePrivilege("SeProfileSingleProcessPrivilege");

            if (purgeStandby)
            {
                try
                {
                    IntPtr pCommand = Marshal.AllocHGlobal(sizeof(int));
                    Marshal.WriteInt32(pCommand, MemoryPurgeStandbyList);
                    uint status = NtSetSystemInformation(SystemMemoryListInformation, pCommand, sizeof(int));
                    Marshal.FreeHGlobal(pCommand);
                    standbyPurged = (status == 0);
                }
                catch
                {
                    standbyPurged = false;
                }
            }
        }

        if (purgeWorkingSets)
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (EmptyWorkingSet(proc.Handle) != 0)
                    {
                        emptiedProcesses++;
                    }
                }
                catch
                {
                    // Ignore processes we can't open
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }

        // Force garbage collection in our own process as well
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var after = GetMemoryStats();
        ulong freed = after.AvailablePhysicalBytes > before.AvailablePhysicalBytes 
            ? after.AvailablePhysicalBytes - before.AvailablePhysicalBytes 
            : 0;

        return new MemoryPurgeResult
        {
            Success = true,
            BytesFreed = freed,
            MemoryBeforeUsed = before.UsedPhysicalBytes,
            MemoryAfterUsed = after.UsedPhysicalBytes,
            Message = $"Memoria optimizada. Procesos limpiados: {emptiedProcesses}. Standby purgado: {(standbyPurged ? "Sí" : "Requiere Admin")}. Liberados: {freed / (1024 * 1024):N0} MB."
        };
    }
}
