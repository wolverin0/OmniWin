using System;
using System.Collections.Generic;
using System.Linq;

namespace OmniWin.Core.Services;

public class ActiveTaskInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TargetHubIndex { get; set; }
    public int TargetTabIndex { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public bool IsCompleted { get; set; }
    public bool IsCancelled { get; set; }
}

public class ActiveTaskCoordinator
{
    public static ActiveTaskCoordinator Instance { get; } = new();

    private readonly object _lock = new();
    private readonly List<ActiveTaskInfo> _tasks = new();

    public event Action? TasksChanged;

    public IReadOnlyList<ActiveTaskInfo> ActiveTasks
    {
        get
        {
            lock (_lock)
            {
                return _tasks.Where(t => !t.IsCompleted && !t.IsCancelled).ToList();
            }
        }
    }

    public ActiveTaskInfo StartTask(string id, string title, string initialStatus, int hubIndex, int tabIndex)
    {
        lock (_lock)
        {
            var existing = _tasks.FirstOrDefault(t => t.Id == id);
            if (existing != null)
            {
                existing.Title = title;
                existing.Status = initialStatus;
                existing.IsCompleted = false;
                existing.IsCancelled = false;
                existing.TargetHubIndex = hubIndex;
                existing.TargetTabIndex = tabIndex;
                existing.StartTime = DateTime.UtcNow;
                TasksChanged?.Invoke();
                return existing;
            }

            var task = new ActiveTaskInfo
            {
                Id = id,
                Title = title,
                Status = initialStatus,
                TargetHubIndex = hubIndex,
                TargetTabIndex = tabIndex
            };
            _tasks.Add(task);
            TasksChanged?.Invoke();
            return task;
        }
    }

    public void UpdateProgress(string id, string status)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task != null && !task.IsCompleted && !task.IsCancelled)
            {
                task.Status = status;
                TasksChanged?.Invoke();
            }
        }
    }

    public void CompleteTask(string id, string? completionStatus = null)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                task.IsCompleted = true;
                if (!string.IsNullOrEmpty(completionStatus))
                {
                    task.Status = completionStatus;
                }
                TasksChanged?.Invoke();
            }
        }
    }

    public void CancelTask(string id)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                task.IsCancelled = true;
                TasksChanged?.Invoke();
            }
        }
    }
}
