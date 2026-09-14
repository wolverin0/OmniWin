using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

#region Data Models

/// <summary>
/// Representa un nodo en la jerarquía o árbol de procesos de Windows.
/// </summary>
public class ProcessTreeNode
{
    public int Pid { get; set; }
    public int ParentPid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public long WorkingSet64 { get; set; }
    public long PrivateBytes64 { get; set; }
    public int HandleCount { get; set; }
    public int ThreadCount { get; set; }
    public int Depth { get; set; }
    public string IndentedName => new string(' ', Depth * 4) + (Depth > 0 ? "└─ " : "") + Name;
    public double MemoryMB => Math.Round(WorkingSet64 / (1024.0 * 1024.0), 2);
    public double PrivateMB => Math.Round(PrivateBytes64 / (1024.0 * 1024.0), 2);
    public List<ProcessTreeNode> Children { get; set; } = new();
    public bool HasChildren => Children.Count > 0;
}

/// <summary>
/// Información detallada de un módulo DLL cargado por un proceso.
/// </summary>
public class ProcessModuleItem
{
    public string ModuleName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileVersion { get; set; } = "N/D";
    public string Description { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string BaseAddress { get; set; } = string.Empty;
    public long ModuleSizeBytes { get; set; }
    public string SizeText => ModuleSizeBytes > 0 ? $"{ModuleSizeBytes / 1024:N0} KB" : "N/D";
}

/// <summary>
/// Representa un socket o conexión TCP activa mapeada a un PID.
/// </summary>
public class ProcessTcpConnectionItem
{
    public int OwningPid { get; set; }
    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string State { get; set; } = string.Empty;
    public string LocalEndPoint => $"{LocalAddress}:{LocalPort}";
    public string RemoteEndPoint => RemotePort == 0 ? $"{RemoteAddress}:*" : $"{RemoteAddress}:{RemotePort}";
    public bool IsListening => State.Equals("LISTENING", StringComparison.OrdinalIgnoreCase);
    public bool IsEstablished => State.Equals("ESTABLISHED", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Detalle de hilo de ejecución de un proceso.
/// </summary>
public class ProcessThreadItem
{
    public int ThreadId { get; set; }
    public int BasePriority { get; set; }
    public int CurrentPriority { get; set; }
    public string State { get; set; } = string.Empty;
    public string WaitReason { get; set; } = string.Empty;
    public double TotalProcessorTimeMs { get; set; }
    public DateTime? StartTime { get; set; }
}

/// <summary>
/// Mapeo detallado de memoria virtual, recuento de handles y rendimiento de un proceso.
/// </summary>
public class ProcessForensicMetrics
{
    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public long WorkingSetBytes { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public long PrivateBytes { get; set; }
    public long PagedMemoryBytes { get; set; }
    public long PeakPagedMemoryBytes { get; set; }
    public long PagedSystemMemoryBytes { get; set; }
    public long NonPagedSystemMemoryBytes { get; set; }
    public int HandleCount { get; set; }
    public int ThreadCount { get; set; }
    public uint PageFaultCount { get; set; }
    public TimeSpan? TotalCpuTime { get; set; }
    public DateTime? StartTime { get; set; }

    public double WorkingSetMB => Math.Round(WorkingSetBytes / (1024.0 * 1024.0), 2);
    public double PeakWorkingSetMB => Math.Round(PeakWorkingSetBytes / (1024.0 * 1024.0), 2);
    public double PrivateMB => Math.Round(PrivateBytes / (1024.0 * 1024.0), 2);
    public double PagedMemoryMB => Math.Round(PagedMemoryBytes / (1024.0 * 1024.0), 2);
    public double SystemPoolMB => Math.Round((PagedSystemMemoryBytes + NonPagedSystemMemoryBytes) / (1024.0 * 1024.0), 2);

    public List<ProcessThreadItem> Threads { get; set; } = new();
}

#endregion

/// <summary>
/// Servicio forense y de diagnóstico profundo de procesos (estilo Process Hacker / System Informer).
/// Provee jerarquía Parent PID, inspección de módulos DLL, conexiones de red TCP en vivo y telemetría de memoria virtual.
/// </summary>
public class ProcessDeepDiagService
{
    #region Win32 P/Invoke & Structures

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MODULEENTRY32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr hProcess, out PROCESS_MEMORY_COUNTERS_EX counters, uint size);

    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // IP Helper API: GetExtendedTcpTable
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint NO_ERROR = 0;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved);

    #endregion

    #region Process Tree Hierarchy

    /// <summary>
    /// Obtiene la lista jerárquica o árbol de procesos activos correlacionando Parent PID con Win32 Toolhelp32.
    /// </summary>
    public List<ProcessTreeNode> GetProcessTree()
    {
        var rawNodes = new Dictionary<int, ProcessTreeNode>();

        // 1. Obtener Parent PID y lista base mediante Toolhelp32 Snapshot
        IntPtr hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (hSnapshot != INVALID_HANDLE_VALUE)
        {
            try
            {
                var pe = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (Process32First(hSnapshot, ref pe))
                {
                    do
                    {
                        int pid = (int)pe.th32ProcessID;
                        int ppid = (int)pe.th32ParentProcessID;
                        rawNodes[pid] = new ProcessTreeNode
                        {
                            Pid = pid,
                            ParentPid = ppid,
                            Name = pe.szExeFile,
                            ThreadCount = (int)pe.cntThreads
                        };
                    } while (Process32Next(hSnapshot, ref pe));
                }
            }
            finally
            {
                CloseHandle(hSnapshot);
            }
        }

        // 2. Enriquecer con métricas de System.Diagnostics.Process
        Process[] runningProcs = Array.Empty<Process>();
        try
        {
            runningProcs = Process.GetProcesses();
        }
        catch { }

        foreach (var p in runningProcs)
        {
            try
            {
                if (!rawNodes.TryGetValue(p.Id, out var node))
                {
                    node = new ProcessTreeNode
                    {
                        Pid = p.Id,
                        Name = p.ProcessName
                    };
                    rawNodes[p.Id] = node;
                }

                try { node.WorkingSet64 = p.WorkingSet64; } catch { }
                try { node.PrivateBytes64 = p.PrivateMemorySize64; } catch { }
                try { node.HandleCount = p.HandleCount; } catch { }
                try { node.Title = p.MainWindowTitle; } catch { }
                try { node.ThreadCount = p.Threads.Count; } catch { }
                try { node.FilePath = p.MainModule?.FileName; } catch { }
            }
            catch { }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }

        // 3. Estructurar árbol protegiendo contra ciclos de PID
        var rootNodes = new List<ProcessTreeNode>();
        foreach (var node in rawNodes.Values)
        {
            if (node.ParentPid > 0 && rawNodes.TryGetValue(node.ParentPid, out var parent) && parent.Pid != node.Pid && !CausesCycle(node.Pid, node.ParentPid, rawNodes))
            {
                parent.Children.Add(node);
            }
            else
            {
                rootNodes.Add(node);
            }
        }

        // Ordenar nodos y calcular profundidades con protección de ciclos y profundidad
        var visitedTree = new HashSet<int>();
        void AssignDepthAndSort(ProcessTreeNode current, int depth)
        {
            if (!visitedTree.Add(current.Pid) || depth > 30) return;
            current.Depth = depth;
            current.Children = current.Children.OrderBy(c => c.Name).ToList();
            foreach (var child in current.Children)
            {
                AssignDepthAndSort(child, depth + 1);
            }
        }

        rootNodes = rootNodes.OrderBy(r => r.Name).ToList();
        foreach (var root in rootNodes)
        {
            AssignDepthAndSort(root, 0);
        }

        return rootNodes;
    }

    private static bool CausesCycle(int childPid, int parentPid, Dictionary<int, ProcessTreeNode> map)
    {
        int curr = parentPid;
        var seen = new HashSet<int> { childPid };
        while (curr > 0 && map.TryGetValue(curr, out var pNode))
        {
            if (!seen.Add(curr)) return true;
            curr = pNode.ParentPid;
        }
        return false;
    }

    /// <summary>
    /// Devuelve la lista aplanada conservando la jerarquía y sangría visual para DataGrids y vistas de lista.
    /// </summary>
    public List<ProcessTreeNode> GetFlattenedProcessTree()
    {
        var roots = GetProcessTree();
        var flattened = new List<ProcessTreeNode>();
        var visitedFlatten = new HashSet<int>();

        void Flatten(ProcessTreeNode node)
        {
            if (!visitedFlatten.Add(node.Pid)) return;
            flattened.Add(node);
            foreach (var child in node.Children)
            {
                Flatten(child);
            }
        }

        foreach (var root in roots)
        {
            Flatten(root);
        }

        return flattened;
    }

    #endregion

    #region Process Modules (DLLs)

    /// <summary>
    /// Enumera los módulos DLL cargados en el espacio de memoria de un proceso (PID).
    /// Extrae nombre, ruta, versión, descripción, compañía y dirección base.
    /// </summary>
    public List<ProcessModuleItem> GetProcessModules(int pid)
    {
        var modules = new List<ProcessModuleItem>();

        // Intento primario: System.Diagnostics.Process (rápido y con metadatos completos)
        try
        {
            using var proc = Process.GetProcessById(pid);
            foreach (ProcessModule m in proc.Modules)
            {
                try
                {
                    string ver = "N/D";
                    string desc = string.Empty;
                    string comp = string.Empty;

                    try
                    {
                        var vi = m.FileVersionInfo;
                        ver = vi.FileVersion ?? "N/D";
                        desc = vi.FileDescription ?? string.Empty;
                        comp = vi.CompanyName ?? string.Empty;
                    }
                    catch { }

                    modules.Add(new ProcessModuleItem
                    {
                        ModuleName = m.ModuleName,
                        FilePath = m.FileName,
                        FileVersion = ver,
                        Description = desc,
                        Company = comp,
                        BaseAddress = $"0x{m.BaseAddress.ToInt64():X16}",
                        ModuleSizeBytes = m.ModuleMemorySize
                    });
                }
                catch { }
            }

            if (modules.Count > 0)
            {
                return modules.OrderBy(m => m.ModuleName).ToList();
            }
        }
        catch
        {
            // En caso de AccessDenied o mismatch de arquitectura 32/64 bits, pasar a Toolhelp32
        }

        // Intento secundario de respaldo nativo: Toolhelp32 Snapshot de módulos
        IntPtr hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)pid);
        if (hSnap != INVALID_HANDLE_VALUE)
        {
            try
            {
                var me = new MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32>() };
                if (Module32First(hSnap, ref me))
                {
                    do
                    {
                        string ver = "N/D";
                        string desc = string.Empty;
                        string comp = string.Empty;

                        if (!string.IsNullOrEmpty(me.szExePath) && File.Exists(me.szExePath))
                        {
                            try
                            {
                                var vi = FileVersionInfo.GetVersionInfo(me.szExePath);
                                ver = vi.FileVersion ?? "N/D";
                                desc = vi.FileDescription ?? string.Empty;
                                comp = vi.CompanyName ?? string.Empty;
                            }
                            catch { }
                        }

                        modules.Add(new ProcessModuleItem
                        {
                            ModuleName = me.szModule,
                            FilePath = me.szExePath,
                            FileVersion = ver,
                            Description = desc,
                            Company = comp,
                            BaseAddress = $"0x{me.modBaseAddr.ToInt64():X16}",
                            ModuleSizeBytes = me.modBaseSize
                        });
                    } while (Module32Next(hSnap, ref me));
                }
            }
            finally
            {
                CloseHandle(hSnap);
            }
        }

        return modules.OrderBy(m => m.ModuleName).ToList();
    }

    #endregion

    #region Real-time TCP Network Connections

    /// <summary>
    /// Enumera todas las conexiones TCP activas y en escucha del sistema, opcionalmente filtradas por PID.
    /// Utiliza P/Invoke nativo de Win32 IP Helper API (GetExtendedTcpTable).
    /// </summary>
    public List<ProcessTcpConnectionItem> GetTcpConnections(int? targetPid = null)
    {
        var connections = new List<ProcessTcpConnectionItem>();

        // 1. IPv4 Connections
        GetExtendedTcpTableIpv4(connections, targetPid);

        // 2. IPv6 Connections
        GetExtendedTcpTableIpv6(connections, targetPid);

        return connections.OrderBy(c => c.LocalPort).ToList();
    }

    private void GetExtendedTcpTableIpv4(List<ProcessTcpConnectionItem> list, int? targetPid)
    {
        int size = 0;
        uint ret = GetExtendedTcpTable(IntPtr.Zero, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return;

        IntPtr pTable = Marshal.AllocHGlobal(size);
        try
        {
            ret = GetExtendedTcpTable(pTable, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (ret == NO_ERROR)
            {
                int numEntries = Marshal.ReadInt32(pTable);
                int rowOffset = 4; // Tamaño del DWORD dwNumEntries
                int rowSize = 24;  // 6 DWORDs: State, LocalAddr, LocalPort, RemoteAddr, RemotePort, OwningPid

                for (int i = 0; i < numEntries; i++)
                {
                    IntPtr pRow = IntPtr.Add(pTable, rowOffset + i * rowSize);
                    uint state = (uint)Marshal.ReadInt32(pRow, 0);
                    uint localAddr = (uint)Marshal.ReadInt32(pRow, 4);
                    uint localPortDword = (uint)Marshal.ReadInt32(pRow, 8);
                    uint remoteAddr = (uint)Marshal.ReadInt32(pRow, 12);
                    uint remotePortDword = (uint)Marshal.ReadInt32(pRow, 16);
                    int owningPid = Marshal.ReadInt32(pRow, 20);

                    if (targetPid.HasValue && targetPid.Value != owningPid)
                        continue;

                    int localPort = ((int)(localPortDword & 0xFF) << 8) | (int)((localPortDword >> 8) & 0xFF);
                    int remotePort = ((int)(remotePortDword & 0xFF) << 8) | (int)((remotePortDword >> 8) & 0xFF);

                    var localIp = new IPAddress(BitConverter.GetBytes(localAddr)).ToString();
                    var remoteIp = new IPAddress(BitConverter.GetBytes(remoteAddr)).ToString();

                    list.Add(new ProcessTcpConnectionItem
                    {
                        OwningPid = owningPid,
                        LocalAddress = localIp,
                        LocalPort = localPort,
                        RemoteAddress = remoteIp,
                        RemotePort = remotePort,
                        State = ResolveTcpState(state)
                    });
                }
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(pTable);
        }
    }

    private void GetExtendedTcpTableIpv6(List<ProcessTcpConnectionItem> list, int? targetPid)
    {
        int size = 0;
        uint ret = GetExtendedTcpTable(IntPtr.Zero, ref size, true, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return;

        IntPtr pTable = Marshal.AllocHGlobal(size);
        try
        {
            ret = GetExtendedTcpTable(pTable, ref size, true, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
            if (ret == NO_ERROR)
            {
                int numEntries = Marshal.ReadInt32(pTable);
                int rowOffset = 4;
                int rowSize = 56; // Layout de MIB_TCP6ROW_OWNER_PID: ucLocalAddr(16) + scope(4) + port(4) + ucRemote(16) + scope(4) + port(4) + state(4) + pid(4)

                byte[] localAddrBytes = new byte[16];
                byte[] remoteAddrBytes = new byte[16];

                for (int i = 0; i < numEntries; i++)
                {
                    IntPtr pRow = IntPtr.Add(pTable, rowOffset + i * rowSize);
                    Marshal.Copy(pRow, localAddrBytes, 0, 16);
                    uint localScopeId = (uint)Marshal.ReadInt32(pRow, 16);
                    uint localPortDword = (uint)Marshal.ReadInt32(pRow, 20);

                    IntPtr pRemoteAddr = IntPtr.Add(pRow, 24);
                    Marshal.Copy(pRemoteAddr, remoteAddrBytes, 0, 16);
                    uint remoteScopeId = (uint)Marshal.ReadInt32(pRow, 40);
                    uint remotePortDword = (uint)Marshal.ReadInt32(pRow, 44);

                    uint state = (uint)Marshal.ReadInt32(pRow, 48);
                    int owningPid = Marshal.ReadInt32(pRow, 52);

                    if (targetPid.HasValue && targetPid.Value != owningPid)
                        continue;

                    int localPort = ((int)(localPortDword & 0xFF) << 8) | (int)((localPortDword >> 8) & 0xFF);
                    int remotePort = ((int)(remotePortDword & 0xFF) << 8) | (int)((remotePortDword >> 8) & 0xFF);

                    var localIp = new IPAddress(localAddrBytes, localScopeId).ToString();
                    var remoteIp = new IPAddress(remoteAddrBytes, remoteScopeId).ToString();

                    list.Add(new ProcessTcpConnectionItem
                    {
                        OwningPid = owningPid,
                        LocalAddress = localIp,
                        LocalPort = localPort,
                        RemoteAddress = remoteIp,
                        RemotePort = remotePort,
                        State = ResolveTcpState(state)
                    });
                }
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(pTable);
        }
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
        _ => $"STATE_{state}"
    };

    #endregion

    #region Virtual Memory & Performance Metrics

    /// <summary>
    /// Mapea la memoria virtual completa (Private Bytes, Working Set, Handles, hilos y contadores de página) de un PID.
    /// </summary>
    public ProcessForensicMetrics GetProcessForensics(int pid)
    {
        var metrics = new ProcessForensicMetrics { Pid = pid };

        try
        {
            using var proc = Process.GetProcessById(pid);
            metrics.Name = proc.ProcessName;

            try { metrics.WorkingSetBytes = proc.WorkingSet64; } catch { }
            try { metrics.PeakWorkingSetBytes = proc.PeakWorkingSet64; } catch { }
            try { metrics.PrivateBytes = proc.PrivateMemorySize64; } catch { }
            try { metrics.PagedMemoryBytes = proc.PagedMemorySize64; } catch { }
            try { metrics.PeakPagedMemoryBytes = proc.PeakPagedMemorySize64; } catch { }
            try { metrics.PagedSystemMemoryBytes = proc.PagedSystemMemorySize64; } catch { }
            try { metrics.NonPagedSystemMemoryBytes = proc.NonpagedSystemMemorySize64; } catch { }
            try { metrics.HandleCount = proc.HandleCount; } catch { }
            try { metrics.ThreadCount = proc.Threads.Count; } catch { }
            try { metrics.StartTime = proc.StartTime; } catch { }
            try { metrics.TotalCpuTime = proc.TotalProcessorTime; } catch { }

            // Enriquecer hilos
            try
            {
                foreach (ProcessThread pt in proc.Threads)
                {
                    try
                    {
                        var thItem = new ProcessThreadItem
                        {
                            ThreadId = pt.Id,
                            BasePriority = pt.BasePriority,
                            CurrentPriority = pt.CurrentPriority,
                            State = pt.ThreadState.ToString()
                        };

                        try
                        {
                            if (pt.ThreadState == System.Diagnostics.ThreadState.Wait)
                                thItem.WaitReason = pt.WaitReason.ToString();
                        }
                        catch { }

                        try { thItem.TotalProcessorTimeMs = pt.TotalProcessorTime.TotalMilliseconds; } catch { }
                        try { thItem.StartTime = pt.StartTime; } catch { }

                        metrics.Threads.Add(thItem);
                    }
                    catch { }
                }
            }
            catch { }
        }
        catch
        {
            // Si el proceso ya cerró o acceso denegado inicial
        }

        // Reforzar con GetProcessMemoryInfo para contadores exactos de Pagefile/PrivateUsage
        IntPtr hProc = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (hProc == IntPtr.Zero)
        {
            hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        }

        if (hProc != IntPtr.Zero)
        {
            try
            {
                if (GetProcessMemoryInfo(hProc, out var memCounters, (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX>()))
                {
                    metrics.PageFaultCount = memCounters.PageFaultCount;
                    if (memCounters.PrivateUsage.ToUInt64() > 0)
                        metrics.PrivateBytes = (long)memCounters.PrivateUsage.ToUInt64();
                    if (memCounters.WorkingSetSize.ToUInt64() > 0)
                        metrics.WorkingSetBytes = (long)memCounters.WorkingSetSize.ToUInt64();
                    if (memCounters.PeakWorkingSetSize.ToUInt64() > 0)
                        metrics.PeakWorkingSetBytes = (long)memCounters.PeakWorkingSetSize.ToUInt64();
                }
            }
            finally
            {
                CloseHandle(hProc);
            }
        }

        return metrics;
    }

    /// <summary>
    /// Finaliza un proceso de manera segura.
    /// </summary>
    public ProcessActionResult KillProcess(int pid, bool killTree = false)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            string name = proc.ProcessName;
            proc.Kill(entireProcessTree: killTree);
            return new ProcessActionResult
            {
                Success = true,
                Message = $"Proceso '{name}' (PID: {pid}) finalizado con éxito."
            };
        }
        catch (Exception ex)
        {
            return new ProcessActionResult
            {
                Success = false,
                Message = $"No se pudo terminar el proceso PID {pid}: {ex.Message}"
            };
        }
    }

    #endregion
}
