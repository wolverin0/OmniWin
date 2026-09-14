using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmniWin.Core.Services;

public enum AwakeMode
{
    Disabled,
    KeepAwakeIndefinite,
    KeepSystemOnly,
    Timed
}

public class AwakeState
{
    public AwakeMode Mode { get; set; } = AwakeMode.Disabled;
    public bool IsActive => Mode != AwakeMode.Disabled;
    public bool KeepDisplayOn { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public TimeSpan? RemainingTime => ExpiresAt.HasValue && ExpiresAt.Value > DateTime.Now 
        ? ExpiresAt.Value - DateTime.Now 
        : (Mode == AwakeMode.Timed ? TimeSpan.Zero : null);
}

public class AwakeService : IDisposable
{
    public static AwakeService Instance { get; } = new();

    [Flags]
    public enum ExecutionState : uint
    {
        EsSystemRequired = 0x00000001,
        EsDisplayRequired = 0x00000002,
        EsAwayModeRequired = 0x00000040,
        EsContinuous = 0x80000000
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    private readonly object _lock = new();
    private Timer? _timer;
    private AwakeState _state = new();

    public AwakeState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return new AwakeState
                {
                    Mode = _state.Mode,
                    KeepDisplayOn = _state.KeepDisplayOn,
                    ExpiresAt = _state.ExpiresAt
                };
            }
        }
    }

    public event Action<AwakeState>? OnStateChanged;

    public bool Activate(AwakeMode mode, TimeSpan? duration = null, bool keepDisplayOn = true)
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;

            if (mode == AwakeMode.Disabled)
            {
                return Deactivate();
            }

            ExecutionState flags = ExecutionState.EsContinuous | ExecutionState.EsSystemRequired;
            if (keepDisplayOn && mode != AwakeMode.KeepSystemOnly)
            {
                flags |= ExecutionState.EsDisplayRequired;
            }

            ExecutionState prev = SetThreadExecutionState(flags);
            if (prev == 0)
            {
                return false;
            }

            DateTime? expires = null;
            if (mode == AwakeMode.Timed && duration.HasValue && duration.Value > TimeSpan.Zero)
            {
                expires = DateTime.Now.Add(duration.Value);
                _timer = new Timer(_ =>
                {
                    Deactivate();
                }, null, (long)duration.Value.TotalMilliseconds, Timeout.Infinite);
            }

            _state = new AwakeState
            {
                Mode = mode,
                KeepDisplayOn = keepDisplayOn && mode != AwakeMode.KeepSystemOnly,
                ExpiresAt = expires
            };

            OnStateChanged?.Invoke(CurrentState);
            return true;
        }
    }

    public bool Deactivate()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;

            SetThreadExecutionState(ExecutionState.EsContinuous);

            _state = new AwakeState
            {
                Mode = AwakeMode.Disabled,
                KeepDisplayOn = false,
                ExpiresAt = null
            };

            OnStateChanged?.Invoke(CurrentState);
            return true;
        }
    }

    public void Dispose()
    {
        Deactivate();
        GC.SuppressFinalize(this);
    }
}
