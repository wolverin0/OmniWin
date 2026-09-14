using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OmniWin.Core.Services;

public class ProcessItem
{
    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
    public int ThreadCount { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public bool Responding { get; set; } = true;
    public double MemoryMB => WorkingSetBytes / (1024.0 * 1024.0);
}

public class ProcessActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ProcessService
{
    public List<ProcessItem> GetRunningProcesses(int limit = 100, string sortBy = "memory")
    {
        var list = new List<ProcessItem>();
        Process[] allProcs;
        try
        {
            allProcs = Process.GetProcesses();
        }
        catch
        {
            return list;
        }

        IEnumerable<Process> topProcs;
        if (sortBy == "name")
            topProcs = allProcs.OrderBy(p => p.ProcessName).Take(limit);
        else
            topProcs = allProcs.OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } }).Take(limit);

        foreach (var proc in topProcs)
        {
            try
            {
                string title = string.Empty;
                try { title = proc.MainWindowTitle; } catch { }

                long workingSet = 0;
                try { workingSet = proc.WorkingSet64; } catch { }

                long privateMemory = 0;
                try { privateMemory = proc.PrivateMemorySize64; } catch { }

                int threadCount = 0;
                try { threadCount = proc.Threads.Count; } catch { }

                list.Add(new ProcessItem
                {
                    Pid = proc.Id,
                    Name = proc.ProcessName,
                    Title = title,
                    WorkingSetBytes = workingSet,
                    PrivateMemoryBytes = privateMemory,
                    ThreadCount = threadCount,
                    Priority = "Normal",
                    FilePath = null,
                    Responding = true
                });
            }
            catch { }
        }

        foreach (var p in allProcs)
        {
            try { p.Dispose(); } catch { }
        }

        return list;
    }

    public ProcessActionResult KillProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            string name = proc.ProcessName;
            proc.Kill();
            return new ProcessActionResult
            {
                Success = true,
                Message = $"Proceso '{name}' (PID: {pid}) terminado con éxito."
            };
        }
        catch (Exception ex)
        {
            return new ProcessActionResult
            {
                Success = false,
                Message = $"Error al terminar proceso PID {pid}: {ex.Message}"
            };
        }
    }

    public ProcessActionResult SetProcessPriority(int pid, ProcessPriorityClass priority)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.PriorityClass = priority;
            return new ProcessActionResult
            {
                Success = true,
                Message = $"Prioridad de '{proc.ProcessName}' (PID: {pid}) cambiada a {priority}."
            };
        }
        catch (Exception ex)
        {
            return new ProcessActionResult
            {
                Success = false,
                Message = $"Error al cambiar prioridad: {ex.Message}"
            };
        }
    }
}
