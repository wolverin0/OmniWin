using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

public class ActiveConnectionItem
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string LocalEndpoint { get; set; } = string.Empty;
    public string RemoteEndpoint { get; set; } = string.Empty;
    public string State { get; set; } = "ESTABLISHED";
    public bool IsBlockedInFirewall { get; set; } = false;
}

public class BlockedFirewallRuleItem
{
    public string RuleName { get; set; } = string.Empty;
    public string ProgramPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class FirewallMonitorService
{
    private static readonly Lazy<FirewallMonitorService> _instance = new(() => new FirewallMonitorService());
    public static FirewallMonitorService Instance => _instance.Value;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        TCP_TABLE_CLASS TableClass,
        uint reserved = 0);

    private const int AF_INET = 2; // IPv4

    private enum TCP_TABLE_CLASS
    {
        TCP_TABLE_BASIC_LISTENER,
        TCP_TABLE_BASIC_CONNECTIONS,
        TCP_TABLE_BASIC_ALL,
        TCP_TABLE_OWNER_PID_LISTENER,
        TCP_TABLE_OWNER_PID_CONNECTIONS,
        TCP_TABLE_OWNER_PID_ALL,
        TCP_TABLE_OWNER_MODULE_LISTENER,
        TCP_TABLE_OWNER_MODULE_CONNECTIONS,
        TCP_TABLE_OWNER_MODULE_ALL
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

    public List<ActiveConnectionItem> GetActiveConnections()
    {
        var connections = new List<ActiveConnectionItem>();
        int bufferSize = 0;

        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        if (bufferSize <= 0) return connections;

        IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
            if (result != 0) return connections;

            int numEntries = Marshal.ReadInt32(tcpTablePtr);
            IntPtr rowPtr = IntPtr.Add(tcpTablePtr, 4);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            var blockedRules = GetOmniWinBlockedRules();
            var procCache = new Dictionary<int, (string Name, string Path)>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr = IntPtr.Add(rowPtr, rowSize);

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
                ushort remotePort = (ushort)((row.remotePort1 << 8) | row.remotePort2);
                var localIp = new IPAddress(row.localAddr);
                var remoteIp = new IPAddress(row.remoteAddr);

                string stateStr = ResolveTcpState(row.state);
                bool isBlocked = !string.IsNullOrEmpty(procInfo.Path) &&
                    blockedRules.Any(b => string.Equals(b.ProgramPath, procInfo.Path, StringComparison.OrdinalIgnoreCase));

                connections.Add(new ActiveConnectionItem
                {
                    ProcessId = pid,
                    ProcessName = procInfo.Name,
                    ExecutablePath = procInfo.Path,
                    LocalEndpoint = $"{localIp}:{localPort}",
                    RemoteEndpoint = $"{remoteIp}:{remotePort}",
                    State = stateStr,
                    IsBlockedInFirewall = isBlocked
                });
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }

        return connections.OrderBy(c => c.ProcessName).ToList();
    }

    private static string ResolveTcpState(uint state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTENING",
        3 => "SYN_SENT",
        4 => "SYN_RCVD",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT1",
        7 => "FIN_WAIT2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "LAST_ACK",
        11 => "TIME_WAIT",
        12 => "DELETE_TCB",
        _ => "UNKNOWN"
    };

    public bool BlockProcessTraffic(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return false;

        string exeName = Path.GetFileName(executablePath);
        string ruleName = $"OmniWin_Block_{exeName}";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block program=\"{executablePath}\" enable=yes",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool BlockProcessInFirewall(string executablePath, string? ruleName = null) => BlockProcessTraffic(executablePath);

    public bool UnblockProcessTraffic(string ruleName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall delete rule name=\"{ruleName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool UnblockProcessInFirewall(string ruleName) => UnblockProcessTraffic(ruleName);

    public List<BlockedFirewallRuleItem> GetOmniWinBlockedRules()
    {
        var rules = new List<BlockedFirewallRuleItem>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "advfirewall firewall show rule name=all",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var p = Process.Start(psi);
            if (p == null) return rules;

            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(4000);

            var blocks = output.Split(new[] { "Rule Name:", "Nombre de regla:" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var b in blocks)
            {
                if (!b.Contains("OmniWin_Block_")) continue;

                string firstLine = b.Split('\n')[0].Trim();
                string progLine = b.Split('\n').FirstOrDefault(l => l.Contains("Program:") || l.Contains("Programa:")) ?? string.Empty;
                string programPath = progLine.Contains(':') ? progLine.Substring(progLine.IndexOf(':') + 1).Trim() : string.Empty;

                rules.Add(new BlockedFirewallRuleItem
                {
                    RuleName = firstLine,
                    ProgramPath = programPath
                });
            }
        }
        catch { }

        return rules;
    }
}
