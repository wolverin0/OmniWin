using System;
using System.Collections.Generic;
using System.Linq;

namespace OmniWin.Core.Services;

public class TelemetrySnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double CpuLoadPercent { get; set; }
    public double GpuLoadPercent { get; set; }
    public double CpuTempC { get; set; }
    public double GpuTempC { get; set; }
    public ulong AvailableRamMb { get; set; }
    public double FrameTimeMs { get; set; }
    public double Fps { get; set; }
    public bool ThermalThrottling { get; set; }
    public int ActiveProcessesCount { get; set; }
}

public class StutterAnalysisReport
{
    public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
    public DateTime? StutterTimestamp { get; set; }
    public double PeakFrameTimeMs { get; set; }
    public double LowestFps { get; set; }
    public string ProbableCause { get; set; } = string.Empty;
    public List<string> CorrelatedFindings { get; set; } = new();
    public List<TelemetrySnapshot> RecentSnapshots { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class StutterInvestigatorService
{
    private static readonly Lazy<StutterInvestigatorService> _instance = new(() => new StutterInvestigatorService());
    public static StutterInvestigatorService Instance => _instance.Value;

    private const int MaxBufferSize = 300; // 30 seconds at 100ms intervals or 300 seconds at 1s
    private readonly LinkedList<TelemetrySnapshot> _ringBuffer = new();
    private readonly object _lock = new();

    public void RecordSnapshot(TelemetrySnapshot snapshot)
    {
        lock (_lock)
        {
            _ringBuffer.AddLast(snapshot);
            if (_ringBuffer.Count > MaxBufferSize)
            {
                _ringBuffer.RemoveFirst();
            }
        }
    }

    public StutterAnalysisReport AnalyzeRecentStutter(TimeSpan? lookback = null)
    {
        var window = lookback ?? TimeSpan.FromSeconds(15);
        DateTime cutoff = DateTime.UtcNow - window;

        List<TelemetrySnapshot> samples;
        lock (_lock)
        {
            samples = _ringBuffer.Where(s => s.Timestamp >= cutoff).ToList();
        }

        var report = new StutterAnalysisReport
        {
            AnalysisTime = DateTime.UtcNow,
            RecentSnapshots = samples
        };

        if (samples.Count == 0)
        {
            report.ProbableCause = "Sin telemetría reciente";
            report.Summary = "No se registraron muestras de telemetría en el ring buffer en los últimos 15 segundos.";
            return report;
        }

        // Identify worst frame spike or highest frame time
        var worstFrame = samples.OrderByDescending(s => s.FrameTimeMs).First();
        var lowestFps = samples.OrderBy(s => s.Fps).First();

        report.StutterTimestamp = worstFrame.Timestamp;
        report.PeakFrameTimeMs = worstFrame.FrameTimeMs;
        report.LowestFps = lowestFps.Fps;

        var findings = new List<string>();
        string cause = "Stutter de Renderizado / Compilación de Shaders";

        // 1. Check Thermal Throttling
        var maxCpuTemp = samples.Max(s => s.CpuTempC);
        var maxGpuTemp = samples.Max(s => s.GpuTempC);
        if (maxCpuTemp >= 90.0 || maxGpuTemp >= 86.0 || worstFrame.ThermalThrottling)
        {
            cause = "Throttling Térmico (CPU/GPU)";
            findings.Add($"Temperatura máxima alcanzada: CPU {maxCpuTemp:F1}°C / GPU {maxGpuTemp:F1}°C. Los relojes de silicio bajaron de frecuencia para proteger el hardware.");
        }

        // 2. Check RAM Exhaustion / Pagefault pressure
        var minRam = samples.Min(s => s.AvailableRamMb);
        if (minRam < 800)
        {
            cause = "Presión Crítica de Memoria RAM / Hard Page Faults";
            findings.Add($"Memoria RAM libre cayó a {minRam} MB. Windows tuvo que realizar paginación en disco (Pagefile) durante el cuadro.");
        }

        // 3. Check CPU 100% Saturation
        var maxCpu = samples.Max(s => s.CpuLoadPercent);
        if (maxCpu >= 98.0 && cause.StartsWith("Stutter de Renderizado"))
        {
            cause = "Saturación de CPU / DPC Latency";
            findings.Add($"Carga de CPU al 100% durante el tirón. Procesos en segundo plano o DPC de controladores compitieron por los núcleos de renderizado.");
        }

        // 4. Check GPU Stall (Frame time high but GPU load low)
        if (worstFrame.FrameTimeMs > 30.0 && worstFrame.GpuLoadPercent < 50.0 && cause.StartsWith("Stutter de Renderizado"))
        {
            cause = "GPU Pipeline Stall (Espera de CPU o E/S de disco)";
            findings.Add($"La GPU redujo su actividad al {worstFrame.GpuLoadPercent:F0}% mientras el frametime se disparó a {worstFrame.FrameTimeMs:F1} ms. La tarjeta gráfica quedó a la espera de datos transferidos desde la CPU o disco NVMe.");
        }

        if (findings.Count == 0)
        {
            findings.Add($"Frametime máximo registrado: {worstFrame.FrameTimeMs:F1} ms ({worstFrame.Fps:F0} FPS). CPU ({maxCpu:F0}%) y temperaturas ({maxCpuTemp:F0}°C) dentro de parámetros estables. Causas probables: carga asíncrona de shaders DirectX/Vulkan o streaming de texturas del motor del juego.");
        }

        report.ProbableCause = cause;
        report.CorrelatedFindings = findings;
        report.Summary = $"Diagnóstico de Stutter: {cause}. Frametime pico: {worstFrame.FrameTimeMs:F1} ms. {string.Join(" ", findings)}";

        return report;
    }

    public void ClearBuffer()
    {
        lock (_lock)
        {
            _ringBuffer.Clear();
        }
    }
}
