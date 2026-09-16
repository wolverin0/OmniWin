using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class ListeningPortItem
{
    public int Port { get; set; }
    public string Protocol { get; set; } = "TCP";
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string LocalAddress { get; set; } = string.Empty;
    public string State { get; set; } = "LISTEN";
    public string ServiceTag { get; set; } = string.Empty;
}

public class PortConflictDiagnosis
{
    public int Port { get; set; }
    public bool IsOccupied { get; set; }
    public ListeningPortItem? OccupyingProcess { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class PortConflictService
{
    public static PortConflictService Instance { get; } = new();

    private const int AF_INET = 2; // IPv4
    private const uint MIB_TCP_STATE_LISTEN = 2;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        TCP_TABLE_CLASS TableClass,
        uint reserved = 0);

    private enum TCP_TABLE_CLASS
    {
        TCP_TABLE_BASIC_LISTENER,
        TCP_TABLE_BASIC_CONNECTIONS,
        TCP_TABLE_BASIC_ALL,
        TCP_TABLE_OWNER_PID_LISTENER,
        TCP_TABLE_OWNER_PID_CONNECTIONS,
        TCP_TABLE_OWNER_PID_ALL
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public byte localPort1;
        public byte localPort2;
        public byte localPort3;
        public byte localPort4;
        public uint remoteAddr;
        public byte remotePort1;
        public byte remotePort2;
        public byte remotePort3;
        public byte remotePort4;
        public uint owningPid;
    }

    private static readonly Dictionary<int, string> KnownPortTags = new()
    {
        { 80, "HTTP Web Server / IIS / Apache / Nginx" },
        { 443, "HTTPS SSL Web Server" },
        { 21, "FTP File Transfer" },
        { 22, "SSH Remote Shell" },
        { 25, "SMTP Mail" },
        { 53, "DNS Resolver" },
        { 135, "RPC Endpoint Mapper" },
        { 445, "SMB Windows File Sharing" },
        { 1433, "Microsoft SQL Server" },
        { 3000, "Node.js / React / Next.js Dev Server" },
        { 3306, "MySQL / MariaDB Database" },
        { 3389, "RDP Remote Desktop" },
        { 5000, "ASP.NET / Flask Web App" },
        { 5173, "Vite / Vue Dev Server" },
        { 5432, "PostgreSQL Database" },
        { 6379, "Redis In-Memory Cache" },
        { 8000, "Python SimpleHTTPServer / Django" },
        { 8080, "HTTP Proxy / Tomcat / Spring Boot / Docker" },
        { 8443, "HTTPS Alternate / Tomcat SSL" },
        { 9000, "PHP-FPM / SonarQube" },
        { 27017, "MongoDB Database" }
    };

    public List<ListeningPortItem> GetListeningPorts()
    {
        var list = new List<ListeningPortItem>();
        int bufferSize = 0;

        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        if (bufferSize <= 0) return list;

        IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
            if (result != 0) return list;

            int numEntries = Marshal.ReadInt32(tcpTablePtr);
            IntPtr rowPtr = IntPtr.Add(tcpTablePtr, 4);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            var procCache = new Dictionary<int, (string Name, string Path)>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr = IntPtr.Add(rowPtr, rowSize);

                // Only take LISTEN state (state == 2)
                if (row.state != MIB_TCP_STATE_LISTEN) continue;

                int pid = (int)row.owningPid;
                if (!procCache.TryGetValue(pid, out var procInfo))
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        string procName = proc.ProcessName;
                        string exePath = string.Empty;
                        try { exePath = proc.MainModule?.FileName ?? string.Empty; } catch { }
                        procInfo = (procName, exePath);
                    }
                    catch
                    {
                        procInfo = ($"PID {pid}", string.Empty);
                    }
                    procCache[pid] = procInfo;
                }

                ushort localPort = (ushort)((row.localPort1 << 8) | row.localPort2);
                string ip = new IPAddress(row.localAddr).ToString();

                string tag = KnownPortTags.TryGetValue(localPort, out var t) ? t : "Servicio de Red";

                list.Add(new ListeningPortItem
                {
                    Port = localPort,
                    Protocol = "TCP",
                    ProcessId = pid,
                    ProcessName = procInfo.Name,
                    ExecutablePath = procInfo.Path,
                    LocalAddress = $"{ip}:{localPort}",
                    State = "LISTEN",
                    ServiceTag = tag
                });
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }

        return list.OrderBy(p => p.Port).ToList();
    }

    public PortConflictDiagnosis DiagnosePort(int port)
    {
        var listeners = GetListeningPorts();
        var holder = listeners.FirstOrDefault(l => l.Port == port);

        if (holder != null)
        {
            return new PortConflictDiagnosis
            {
                Port = port,
                IsOccupied = true,
                OccupyingProcess = holder,
                Description = $"Puerto {port} bloqueado por '{holder.ProcessName}' (PID {holder.ProcessId}) [{holder.ServiceTag}]."
            };
        }

        return new PortConflictDiagnosis
        {
            Port = port,
            IsOccupied = false,
            Description = $"Puerto {port} disponible y libre de conflictos."
        };
    }

    public List<PortConflictDiagnosis> DiagnoseCommonPorts()
    {
        var listeners = GetListeningPorts();
        var results = new List<PortConflictDiagnosis>();

        foreach (var kvp in KnownPortTags.OrderBy(k => k.Key))
        {
            var holder = listeners.FirstOrDefault(l => l.Port == kvp.Key);
            results.Add(new PortConflictDiagnosis
            {
                Port = kvp.Key,
                IsOccupied = holder != null,
                OccupyingProcess = holder,
                Description = holder != null
                    ? $"En uso por {holder.ProcessName} (PID {holder.ProcessId})"
                    : "Libre"
            });
        }

        return results;
    }

    public async Task<bool> ReleasePortAsync(int port)
    {
        return await Task.Run(() =>
        {
            var diagnosis = DiagnosePort(port);
            if (!diagnosis.IsOccupied || diagnosis.OccupyingProcess == null)
                return true; // Already free

            int pid = diagnosis.OccupyingProcess.ProcessId;
            if (pid <= 4) return false; // Never terminate System or Idle

            try
            {
                var proc = Process.GetProcessById(pid);
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(3000);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RELEASE_PORT_ERROR] {ex.Message}");
                return false;
            }
        });
    }
}
