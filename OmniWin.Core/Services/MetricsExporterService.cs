using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

/// <summary>
/// Servidor HTTP embebido ultra liviano para exportar métricas en formato estándar de Prometheus (puerto 9182 /metrics).
/// </summary>
public class MetricsExporterService : IDisposable
{
    private static readonly Lazy<MetricsExporterService> _lazyInstance = new(() => new MetricsExporterService());
    public static MetricsExporterService Instance => _lazyInstance.Value;

    private readonly MemoryService _memoryService = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly object _syncLock = new();

    // Estado para muestreo de CPU
    private static long _prevIdleTime;
    private static long _prevKernelTime;
    private static long _prevUserTime;
    private static bool _hasCpuSample;
    private static readonly object _cpuLock = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    public int Port { get; private set; } = 9182;
    public bool IsRunning => _listener?.IsListening ?? false;
    public string MetricsUrl => $"http://localhost:{Port}/metrics";
    public DateTime? StartedAt { get; private set; }
    public long ScrapesCount { get; private set; }

    public event Action<bool>? StateChanged;

    public void Start(int port = 9182)
    {
        lock (_syncLock)
        {
            if (IsRunning)
            {
                if (Port == port) return;
                Stop();
            }

            Port = port;
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();

            _listener.Prefixes.Add($"http://localhost:{port}/");

            try
            {
                _listener.Start();
                StartedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                _listener.Close();
                _listener = null;
                throw new InvalidOperationException($"No se pudo iniciar el servidor HTTP en el puerto {port}: {ex.Message}", ex);
            }

            _listenTask = Task.Run(() => ListenLoopAsync(_listener, _cts.Token));
            StateChanged?.Invoke(true);
        }
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            if (_listener == null) return;

            try
            {
                _cts?.Cancel();
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
                // Ignorar excepciones al detener
            }
            finally
            {
                _listener = null;
                _cts?.Dispose();
                _cts = null;
                StartedAt = null;
                StateChanged?.Invoke(false);
            }
        }
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleContextAsync(context), token);
            }
            catch (HttpListenerException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                if (token.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
        {
            var req = context.Request;
            var res = context.Response;

            string path = req.Url?.AbsolutePath?.TrimEnd('/') ?? "";

            // Acepta /metrics y raíz /
            if (string.IsNullOrEmpty(path) || path.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
            {
                ScrapesCount++;
                string metricsText = GenerateMetricsText();
                byte[] buffer = Encoding.UTF8.GetBytes(metricsText);

                res.StatusCode = 200;
                res.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                res.ContentLength64 = buffer.Length;
                await res.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                res.OutputStream.Close();
            }
            else
            {
                res.StatusCode = 404;
                byte[] notFound = Encoding.UTF8.GetBytes("Not Found. Prometheus metrics endpoint is available at /metrics\n");
                res.ContentType = "text/plain; charset=utf-8";
                res.ContentLength64 = notFound.Length;
                await res.OutputStream.WriteAsync(notFound, 0, notFound.Length).ConfigureAwait(false);
                res.OutputStream.Close();
            }
        }
        catch
        {
            // Ignorar cancelaciones de socket del cliente
        }
    }

    private static double _cachedCpuPercent = 0.0;
    private static DateTime _lastProcessSampleTime = DateTime.MinValue;
    private static (long handles, long threads) _cachedHandlesAndThreads = (0, 0);
    private static readonly object _processSampleLock = new();

    public double GetCpuUsagePercent()
    {
        lock (_cpuLock)
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
                _cachedCpuPercent = Math.Clamp(load, 0.0, 100.0);
                return _cachedCpuPercent;
            }
            catch
            {
                return _cachedCpuPercent;
            }
        }
    }

    public (long handles, long threads) GetSystemHandlesAndThreads()
    {
        lock (_processSampleLock)
        {
            // Cache results for 2 seconds to avoid iterate-all-processes overhead on rapid Prometheus scrapes
            if (DateTime.UtcNow - _lastProcessSampleTime < TimeSpan.FromSeconds(2) && _cachedHandlesAndThreads.handles > 0)
            {
                return _cachedHandlesAndThreads;
            }

            long handles = 0;
            long threads = 0;

            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        handles += proc.HandleCount;
                        threads += proc.Threads.Count;
                    }
                    catch
                    {
                        // Procesos del sistema protegidos
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                _cachedHandlesAndThreads = (handles, threads);
                _lastProcessSampleTime = DateTime.UtcNow;
            }
            catch
            {
                // Retornar último cache si falla
            }

            return _cachedHandlesAndThreads;
        }
    }

    public string GenerateMetricsText()
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        // 1. CPU Usage
        double cpuPercent = GetCpuUsagePercent();
        sb.AppendLine("# HELP windows_cpu_usage_percent Current CPU usage percentage (0-100).");
        sb.AppendLine("# TYPE windows_cpu_usage_percent gauge");
        sb.AppendLine($"windows_cpu_usage_percent {cpuPercent.ToString("F2", inv)}");
        sb.AppendLine();

        // 2. Memory metrics
        var mem = _memoryService.GetMemoryStats();
        sb.AppendLine("# HELP windows_memory_physical_total_bytes Total physical RAM installed in bytes.");
        sb.AppendLine("# TYPE windows_memory_physical_total_bytes gauge");
        sb.AppendLine($"windows_memory_physical_total_bytes {mem.TotalPhysicalBytes.ToString(inv)}");
        sb.AppendLine();

        sb.AppendLine("# HELP windows_memory_physical_used_bytes Physical RAM currently used in bytes.");
        sb.AppendLine("# TYPE windows_memory_physical_used_bytes gauge");
        sb.AppendLine($"windows_memory_physical_used_bytes {mem.UsedPhysicalBytes.ToString(inv)}");
        sb.AppendLine();

        sb.AppendLine("# HELP windows_memory_physical_available_bytes Physical RAM currently available in bytes.");
        sb.AppendLine("# TYPE windows_memory_physical_available_bytes gauge");
        sb.AppendLine($"windows_memory_physical_available_bytes {mem.AvailablePhysicalBytes.ToString(inv)}");
        sb.AppendLine();

        // 3. Disk Free Bytes
        sb.AppendLine("# HELP windows_disk_free_bytes Free storage space available in bytes per volume.");
        sb.AppendLine("# TYPE windows_disk_free_bytes gauge");
        bool cDriveFound = false;
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    string driveName = drive.Name.TrimEnd('\\');
                    if (driveName.Equals("C:", StringComparison.OrdinalIgnoreCase))
                        cDriveFound = true;

                    sb.AppendLine($"windows_disk_free_bytes{{drive=\"{driveName}\"}} {drive.AvailableFreeSpace.ToString(inv)}");
                }
            }
        }
        catch
        {
            // Acceso de disco
        }

        if (!cDriveFound)
        {
            sb.AppendLine("windows_disk_free_bytes{drive=\"C:\"} 0");
        }
        sb.AppendLine();

        // 4. System Uptime
        double uptimeSeconds = Environment.TickCount64 / 1000.0;
        sb.AppendLine("# HELP windows_system_uptime_seconds Operating system uptime in seconds.");
        sb.AppendLine("# TYPE windows_system_uptime_seconds gauge");
        sb.AppendLine($"windows_system_uptime_seconds {uptimeSeconds.ToString("F1", inv)}");
        sb.AppendLine();

        // 5. Handles & Threads
        var (handles, threads) = GetSystemHandlesAndThreads();
        sb.AppendLine("# HELP windows_system_handles_count Total open handle count across all system processes.");
        sb.AppendLine("# TYPE windows_system_handles_count gauge");
        sb.AppendLine($"windows_system_handles_count {handles.ToString(inv)}");
        sb.AppendLine();

        sb.AppendLine("# HELP windows_system_threads_count Total active thread count across all system processes.");
        sb.AppendLine("# TYPE windows_system_threads_count gauge");
        sb.AppendLine($"windows_system_threads_count {threads.ToString(inv)}");
        sb.AppendLine();

        // 6. Thermal, GPU and Latency Telemetry
        var hub = TelemetryHub.Instance.CurrentSnapshot;
        if (hub.CpuTemperatureCelsius.HasValue)
        {
            sb.AppendLine("# HELP windows_cpu_temperature_celsius CPU package temperature in Celsius.");
            sb.AppendLine("# TYPE windows_cpu_temperature_celsius gauge");
            sb.AppendLine($"windows_cpu_temperature_celsius {hub.CpuTemperatureCelsius.Value.ToString("F1", inv)}");
            sb.AppendLine();
        }

        if (hub.CpuPowerWatts.HasValue)
        {
            sb.AppendLine("# HELP windows_cpu_power_watts CPU package power draw in Watts.");
            sb.AppendLine("# TYPE windows_cpu_power_watts gauge");
            sb.AppendLine($"windows_cpu_power_watts {hub.CpuPowerWatts.Value.ToString("F1", inv)}");
            sb.AppendLine();
        }

        if (hub.GpuTemperatureCelsius.HasValue)
        {
            sb.AppendLine("# HELP windows_gpu_temperature_celsius GPU core temperature in Celsius.");
            sb.AppendLine("# TYPE windows_gpu_temperature_celsius gauge");
            sb.AppendLine($"windows_gpu_temperature_celsius {hub.GpuTemperatureCelsius.Value.ToString("F1", inv)}");
            sb.AppendLine();
        }

        if (hub.GpuLoadPercent.HasValue)
        {
            sb.AppendLine("# HELP windows_gpu_usage_percent GPU core utilization percentage.");
            sb.AppendLine("# TYPE windows_gpu_usage_percent gauge");
            sb.AppendLine($"windows_gpu_usage_percent {hub.GpuLoadPercent.Value.ToString("F1", inv)}");
            sb.AppendLine();
        }

        if (hub.KernelJitterUs > 0)
        {
            sb.AppendLine("# HELP windows_kernel_scheduler_jitter_microseconds Kernel thread scheduler wake jitter in microseconds.");
            sb.AppendLine("# TYPE windows_kernel_scheduler_jitter_microseconds gauge");
            sb.AppendLine($"windows_kernel_scheduler_jitter_microseconds {hub.KernelJitterUs.ToString("F1", inv)}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        Stop();
    }
}
