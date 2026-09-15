using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

/// <summary>
/// Immutable snapshot representing unified system and hardware telemetry at a single point in time.
/// </summary>
public record SystemTelemetrySnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public double CpuLoadPercent { get; init; }
    public double? CpuTemperatureCelsius { get; init; }
    public double? CpuPowerWatts { get; init; }
    public double? GpuLoadPercent { get; init; }
    public double? GpuTemperatureCelsius { get; init; }
    public double? GpuMemoryUsedMb { get; init; }
    public ulong RamTotalBytes { get; init; }
    public ulong RamUsedBytes { get; init; }
    public ulong RamAvailableBytes { get; init; }
    public double RamUsagePercent { get; init; }
    public double KernelJitterUs { get; init; }
    public double FrameTimeMs { get; init; }
    public double Fps { get; init; }
    public int ActiveProcessesCount { get; init; }
    public int TotalThreadsCount { get; init; }
    public long TotalHandlesCount { get; init; }
    public bool IsThermalThrottling { get; init; }
}

/// <summary>
/// TelemetryHub: Central single-source-of-truth telemetry dispatcher for OmniWin.
/// Coordinates background sampling across CPU, GPU, RAM, Kernel Jitter, and Frame Pacing.
/// Decouples UI, CompanionServer, Prometheus exporter, and StutterInvestigator from ad-hoc polling.
/// </summary>
public class TelemetryHub : IDisposable
{
    private static readonly Lazy<TelemetryHub> _instance = new(() => new TelemetryHub());
    public static TelemetryHub Instance => _instance.Value;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    private readonly object _snapshotLock = new();
    private SystemTelemetrySnapshot _currentSnapshot = new();
    private CancellationTokenSource? _cts;
    private Task? _samplingTask;
    private bool _isDisposed;

    // CPU sampling state
    private long _prevIdleTime;
    private long _prevKernelTime;
    private long _prevUserTime;
    private bool _hasCpuSample;
    private double _cachedCpuPercent;

    // Process sampling throttle state
    private DateTime _lastProcessSample = DateTime.MinValue;
    private int _cachedProcCount;
    private int _cachedThreadCount;
    private long _cachedHandleCount;

    public event Action<SystemTelemetrySnapshot>? OnTelemetryUpdated;

    public SystemTelemetrySnapshot CurrentSnapshot
    {
        get
        {
            lock (_snapshotLock)
            {
                return _currentSnapshot;
            }
        }
    }

    public bool IsRunning => _samplingTask != null && !_samplingTask.IsCompleted;

    public TelemetryHub()
    {
        // Initial prime sample so CurrentSnapshot is immediately available
        try
        {
            _currentSnapshot = CollectTelemetry();
        }
        catch { }

        // Automatically start sampling loop at 1000ms default interval
        Start(1000);
    }

    public void Start(int intervalMs = 1000)
    {
        if (intervalMs < 100) intervalMs = 100;

        lock (_snapshotLock)
        {
            if (IsRunning) return;

            // Start continuous 1Hz scheduler jitter provider
            KernelLatencyService.Instance.StartContinuousSampler(1);

            _cts = new CancellationTokenSource();
            _samplingTask = Task.Run(() => SamplingLoopAsync(intervalMs, _cts.Token));
        }
    }

    public void Stop()
    {
        lock (_snapshotLock)
        {
            if (!IsRunning) return;

            try
            {
                KernelLatencyService.Instance.StopContinuousSampler();
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { }
            finally
            {
                _cts = null;
                _samplingTask = null;
            }
        }
    }

    public SystemTelemetrySnapshot SampleNow()
    {
        var snap = CollectTelemetry();
        lock (_snapshotLock)
        {
            _currentSnapshot = snap;
        }

        // Push real metrics to StutterInvestigatorService ring buffer
        ForwardToStutterInvestigator(snap);

        OnTelemetryUpdated?.Invoke(snap);
        return snap;
    }

    private async Task SamplingLoopAsync(int intervalMs, CancellationToken ct)
    {
        // First prime sample
        SampleNow();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(intervalMs, ct).ConfigureAwait(false);
                SampleNow();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelemetryHub] Sampling tick exception: {ex.Message}");
            }
        }
    }

    private SystemTelemetrySnapshot CollectTelemetry()
    {
        // 1. CPU Usage via GetSystemTimes
        double cpuPercent = CalculateCpuPercent();

        // 2. Memory metrics
        var memService = new MemoryService();
        var memStats = memService.GetMemoryStats();

        // 3. Thermal and GPU telemetry
        var thermalSnap = ThermalSensorService.Instance.GetSnapshot();

        // 4. Kernel Latency / Jitter
        double jitter = KernelLatencyService.Instance.CurrentJitterUs;

        // 5. Throttling detection
        bool isThrottling = (thermalSnap.CpuPackageTemp >= 90.0 || thermalSnap.GpuCoreTemp >= 86.0);

        // 6. Process / Thread / Handle counts (sampled every 3 seconds to avoid kernel overhead)
        SampleProcessCountersIfDue();

        return new SystemTelemetrySnapshot
        {
            Timestamp = DateTime.UtcNow,
            CpuLoadPercent = cpuPercent,
            CpuTemperatureCelsius = thermalSnap.CpuPackageTemp,
            CpuPowerWatts = thermalSnap.CpuPowerWatts,
            GpuLoadPercent = thermalSnap.GpuCoreLoad,
            GpuTemperatureCelsius = thermalSnap.GpuCoreTemp,
            GpuMemoryUsedMb = thermalSnap.GpuMemoryUsedMb,
            RamTotalBytes = memStats.TotalPhysicalBytes,
            RamUsedBytes = memStats.UsedPhysicalBytes,
            RamAvailableBytes = memStats.AvailablePhysicalBytes,
            RamUsagePercent = memStats.UsagePercentage,
            KernelJitterUs = jitter,
            FrameTimeMs = 0.0, // Populated during active benchmarks or gaming hooks
            Fps = 0.0,
            ActiveProcessesCount = _cachedProcCount,
            TotalThreadsCount = _cachedThreadCount,
            TotalHandlesCount = _cachedHandleCount,
            IsThermalThrottling = isThrottling
        };
    }

    private void ForwardToStutterInvestigator(SystemTelemetrySnapshot snap)
    {
        try
        {
            StutterInvestigatorService.Instance.RecordSnapshot(new TelemetrySnapshot
            {
                Timestamp = snap.Timestamp,
                CpuLoadPercent = snap.CpuLoadPercent,
                GpuLoadPercent = snap.GpuLoadPercent ?? 0.0,
                CpuTempC = snap.CpuTemperatureCelsius ?? 0.0,
                GpuTempC = snap.GpuTemperatureCelsius ?? 0.0,
                AvailableRamMb = (ulong)(snap.RamAvailableBytes / (1024 * 1024)),
                FrameTimeMs = snap.FrameTimeMs,
                Fps = snap.Fps,
                ThermalThrottling = snap.IsThermalThrottling,
                ActiveProcessesCount = snap.ActiveProcessesCount
            });
        }
        catch { }
    }

    private double CalculateCpuPercent()
    {
        try
        {
            if (!GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
                return _cachedCpuPercent;

            if (!_hasCpuSample)
            {
                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;
                _hasCpuSample = true;
                return _cachedCpuPercent;
            }

            long usr = userTime - _prevUserTime;
            long ker = kernelTime - _prevKernelTime;
            long idl = idleTime - _prevIdleTime;

            _prevIdleTime = idleTime;
            _prevKernelTime = kernelTime;
            _prevUserTime = userTime;

            long total = usr + ker;
            if (total <= 0) return _cachedCpuPercent;

            double load = (double)(total - idl) / total * 100.0;
            _cachedCpuPercent = Math.Clamp(Math.Round(load, 1), 0.0, 100.0);
            return _cachedCpuPercent;
        }
        catch
        {
            return _cachedCpuPercent;
        }
    }

    private void SampleProcessCountersIfDue()
    {
        if ((DateTime.UtcNow - _lastProcessSample).TotalSeconds < 3.0) return;

        try
        {
            var procs = Process.GetProcesses();
            _cachedProcCount = procs.Length;

            int totalThreads = 0;
            long totalHandles = 0;
            foreach (var p in procs)
            {
                try
                {
                    totalThreads += p.Threads.Count;
                    totalHandles += p.HandleCount;
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            _cachedThreadCount = totalThreads;
            _cachedHandleCount = totalHandles;
            _lastProcessSample = DateTime.UtcNow;
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}
