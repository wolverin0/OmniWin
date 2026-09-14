using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

public record LockingProcessInfo(
    int ProcessId,
    string ProcessName,
    string AppName,
    string ApplicationType,
    bool CanBeTerminated
);

public record UnlockResult(
    string FilePath,
    bool Success,
    int ProcessesFound,
    int ProcessesKilled,
    string Message
);

public class FileLockService
{
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;
        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    private enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Auto)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Auto)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFileNames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        out uint lpdwRebootReasons);

    /// <summary>
    /// Returns the list of processes currently locking the given file path.
    /// </summary>
    public List<LockingProcessInfo> GetLockingProcesses(string filePath)
    {
        var result = new List<LockingProcessInfo>();
        if (string.IsNullOrWhiteSpace(filePath) || (!System.IO.File.Exists(filePath) && !System.IO.Directory.Exists(filePath)))
        {
            return result;
        }

        string sessionKey = Guid.NewGuid().ToString();
        int res = RmStartSession(out uint handle, 0, sessionKey);
        if (res != 0) return result;

        try
        {
            string[] resources = [filePath];
            res = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);
            if (res != 0) return result;

            uint pnProcInfoNeeded = 0;
            uint pnProcInfo = 0;
            uint lpdwRebootReasons = 0;

            res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, out lpdwRebootReasons);
            if (res == 234) // ERROR_MORE_DATA
            {
                var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;

                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, out lpdwRebootReasons);
                if (res == 0)
                {
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        var info = processInfo[i];
                        string procName = "Desconocido";
                        try
                        {
                            using var proc = Process.GetProcessById(info.Process.dwProcessId);
                            procName = proc.ProcessName;
                        }
                        catch { }

                        bool canTerminate = info.ApplicationType != RM_APP_TYPE.RmCritical && info.Process.dwProcessId > 4;

                        result.Add(new LockingProcessInfo(
                            ProcessId: info.Process.dwProcessId,
                            ProcessName: procName,
                            AppName: string.IsNullOrWhiteSpace(info.strAppName) ? procName : info.strAppName,
                            ApplicationType: info.ApplicationType.ToString(),
                            CanBeTerminated: canTerminate
                        ));
                    }
                }
            }
        }
        catch { }
        finally
        {
            RmEndSession(handle);
        }

        return result;
    }

    /// <summary>
    /// Attempts to release the lock on the specified file by terminating the blocking processes.
    /// </summary>
    public UnlockResult UnlockFile(string filePath, bool killProcesses = true)
    {
        var locking = GetLockingProcesses(filePath);
        if (locking.Count == 0)
        {
            return new UnlockResult(filePath, true, 0, 0, "No hay procesos bloqueando este archivo.");
        }

        int killed = 0;
        if (killProcesses)
        {
            foreach (var procInfo in locking)
            {
                if (!procInfo.CanBeTerminated) continue;

                try
                {
                    using var proc = Process.GetProcessById(procInfo.ProcessId);
                    proc.Kill(true);
                    killed++;
                }
                catch { }
            }
        }

        // Verify if still locked
        var remaining = GetLockingProcesses(filePath);
        bool success = remaining.Count == 0;
        string msg = success
            ? $"Archivo liberado con éxito. Se terminaron {killed} proceso(s) bloqueador(es)."
            : $"Quedan {remaining.Count} proceso(s) bloqueando el archivo ({string.Join(", ", remaining.ConvertAll(r => r.ProcessName))}).";

        return new UnlockResult(filePath, success, locking.Count, killed, msg);
    }
}
