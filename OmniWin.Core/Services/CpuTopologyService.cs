using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

public class CpuSetInfoItem
{
    public uint Id { get; set; }
    public ushort Group { get; set; }
    public byte LogicalProcessorIndex { get; set; }
    public byte CoreIndex { get; set; }
    public byte LastLevelCacheIndex { get; set; }
    public byte NumaNodeIndex { get; set; }
    public byte EfficiencyClass { get; set; }
    public bool IsParked { get; set; }
    public bool IsPerformanceCore { get; set; }
}

public class CpuTopologyInfo
{
    public int LogicalProcessorCount { get; set; }
    public int PhysicalCoreCount { get; set; }
    public bool IsHybrid { get; set; }
    public int PerformanceThreadsCount { get; set; }
    public int EfficiencyThreadsCount { get; set; }
    public long PerformanceAffinityMask { get; set; }
    public long EfficiencyAffinityMask { get; set; }
    public List<CpuSetInfoItem> Processors { get; set; } = new();

    public string Summary => IsHybrid
        ? $"Arquitectura Híbrida detectada: {PerformanceThreadsCount} hilos de Alto Rendimiento (P-Cores) + {EfficiencyThreadsCount} hilos de Eficiencia (E-Cores). Total {LogicalProcessorCount} hilos / {PhysicalCoreCount} núcleos físicos."
        : $"Arquitectura Homogénea: {LogicalProcessorCount} hilos en {PhysicalCoreCount} núcleos físicos.";
}

public class CpuTopologyService
{
    private static readonly Lazy<CpuTopologyService> _instance = new(() => new CpuTopologyService());
    public static CpuTopologyService Instance => _instance.Value;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemCpuSetInformation(
        IntPtr Information,
        uint BufferLength,
        out uint ReturnedLength,
        IntPtr Process,
        uint Flags);

    private CpuTopologyInfo? _cachedTopology;
    private readonly object _lock = new();

    public CpuTopologyInfo GetTopology(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (_cachedTopology != null && !forceRefresh)
            {
                return _cachedTopology;
            }

            var topo = InspectSystemCpuSets();
            _cachedTopology = topo;
            return topo;
        }
    }

    public long GetPerformanceCoreMask()
    {
        var topo = GetTopology();
        return topo.IsHybrid ? topo.PerformanceAffinityMask : 0L;
    }

    public long GetEfficiencyCoreMask()
    {
        var topo = GetTopology();
        return topo.IsHybrid ? topo.EfficiencyAffinityMask : 0L;
    }

    private CpuTopologyInfo InspectSystemCpuSets()
    {
        var result = new CpuTopologyInfo
        {
            LogicalProcessorCount = Environment.ProcessorCount
        };

        try
        {
            uint neededBytes = 0;
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out neededBytes, IntPtr.Zero, 0);
            if (neededBytes == 0)
            {
                return FallbackTopology(result);
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)neededBytes);
            try
            {
                if (!GetSystemCpuSetInformation(buffer, neededBytes, out uint returnedBytes, IntPtr.Zero, 0))
                {
                    return FallbackTopology(result);
                }

                int offset = 0;
                var list = new List<CpuSetInfoItem>();
                byte maxEffClass = 0;

                while (offset < returnedBytes)
                {
                    // Struct layout:
                    // DWORD Size (offset 0)
                    // DWORD Type (offset 4) - 0 is CpuSetInformation
                    uint size = (uint)Marshal.ReadInt32(buffer, offset);
                    if (size == 0) break;

                    int type = Marshal.ReadInt32(buffer, offset + 4);
                    if (type == 0) // CpuSetInformation
                    {
                        uint id = (uint)Marshal.ReadInt32(buffer, offset + 8);
                        ushort group = (ushort)Marshal.ReadInt16(buffer, offset + 12);
                        byte logicalIndex = Marshal.ReadByte(buffer, offset + 14);
                        byte coreIndex = Marshal.ReadByte(buffer, offset + 15);
                        byte llcIndex = Marshal.ReadByte(buffer, offset + 16);
                        byte numaIndex = Marshal.ReadByte(buffer, offset + 17);
                        byte effClass = Marshal.ReadByte(buffer, offset + 18);
                        byte allFlags = Marshal.ReadByte(buffer, offset + 19);

                        if (effClass > maxEffClass)
                        {
                            maxEffClass = effClass;
                        }

                        list.Add(new CpuSetInfoItem
                        {
                            Id = id,
                            Group = group,
                            LogicalProcessorIndex = logicalIndex,
                            CoreIndex = coreIndex,
                            LastLevelCacheIndex = llcIndex,
                            NumaNodeIndex = numaIndex,
                            EfficiencyClass = effClass,
                            IsParked = (allFlags & 1) != 0
                        });
                    }

                    offset += (int)size;
                }

                if (list.Count == 0)
                {
                    return FallbackTopology(result);
                }

                result.Processors = list;
                result.LogicalProcessorCount = list.Count;

                // Distinct cores count
                var distinctCores = list.Select(x => (x.Group, x.CoreIndex)).Distinct().Count();
                result.PhysicalCoreCount = distinctCores > 0 ? distinctCores : list.Count;

                // Check for hybrid architecture (multiple efficiency classes present)
                var distinctClasses = list.Select(x => x.EfficiencyClass).Distinct().ToList();
                result.IsHybrid = distinctClasses.Count > 1;

                long pMask = 0;
                long eMask = 0;
                int pCount = 0;
                int eCount = 0;

                foreach (var item in list)
                {
                    // Highest EfficiencyClass corresponds to Performance Cores (P-Cores)
                    bool isP = result.IsHybrid ? (item.EfficiencyClass == maxEffClass) : true;
                    item.IsPerformanceCore = isP;

                    if (item.LogicalProcessorIndex < 64)
                    {
                        long bit = 1L << item.LogicalProcessorIndex;
                        if (isP)
                        {
                            pMask |= bit;
                            pCount++;
                        }
                        else
                        {
                            eMask |= bit;
                            eCount++;
                        }
                    }
                }

                result.PerformanceAffinityMask = pMask;
                result.EfficiencyAffinityMask = eMask;
                result.PerformanceThreadsCount = pCount;
                result.EfficiencyThreadsCount = eCount;

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return FallbackTopology(result);
        }
    }

    private static CpuTopologyInfo FallbackTopology(CpuTopologyInfo info)
    {
        int count = Math.Max(1, info.LogicalProcessorCount);
        info.PhysicalCoreCount = count;
        info.IsHybrid = false;
        info.PerformanceThreadsCount = count;
        info.EfficiencyThreadsCount = 0;
        info.PerformanceAffinityMask = count >= 64 ? -1L : ((1L << count) - 1);
        info.EfficiencyAffinityMask = 0;
        return info;
    }
}
