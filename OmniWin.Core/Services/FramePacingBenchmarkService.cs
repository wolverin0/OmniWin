using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OmniWin.Core.Services;

public class BenchmarkReport
{
    public string SessionName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public TimeSpan Duration { get; set; }
    public int TotalFrames { get; set; }
    public double AverageFps { get; set; }
    public double OnePercentLowFps { get; set; }
    public double PointOnePercentLowFps { get; set; }
    public double MaxFrametimeMs { get; set; }
    public double MinFrametimeMs { get; set; }
    public double AverageFrametimeMs { get; set; }
    public int StutterCount { get; set; }
    public double StutterPercentage { get; set; }
    public List<double> FrametimesMs { get; set; } = new();
}

public class FramePacingBenchmarkService
{
    public static FramePacingBenchmarkService Instance { get; } = new();

    private readonly object _lock = new();
    private readonly List<double> _currentSamples = new();
    private Stopwatch? _stopwatch;
    private long _lastFrameTimestamp;

    public bool IsBenchmarking { get; private set; }
    public string ActiveSessionName { get; private set; } = string.Empty;

    public void StartBenchmark(string sessionName = "Game Benchmark")
    {
        lock (_lock)
        {
            _currentSamples.Clear();
            ActiveSessionName = sessionName;
            IsBenchmarking = true;
            _lastFrameTimestamp = Stopwatch.GetTimestamp();
            _stopwatch = Stopwatch.StartNew();
        }
    }

    public void RecordFrame()
    {
        if (!IsBenchmarking) return;

        long now = Stopwatch.GetTimestamp();
        long elapsedTicks = now - _lastFrameTimestamp;
        _lastFrameTimestamp = now;

        double ms = (elapsedTicks * 1000.0) / Stopwatch.Frequency;
        if (ms > 0.1 && ms < 500.0) // Filter out pause / alt-tab outliers
        {
            lock (_lock)
            {
                _currentSamples.Add(ms);
            }
        }
    }

    public void AddFrametimeSample(double frametimeMs)
    {
        if (!IsBenchmarking) return;
        if (frametimeMs <= 0.1 || frametimeMs > 500.0) return;

        lock (_lock)
        {
            _currentSamples.Add(frametimeMs);
        }
    }

    public BenchmarkReport StopBenchmark()
    {
        lock (_lock)
        {
            IsBenchmarking = false;
            _stopwatch?.Stop();
            var duration = _stopwatch?.Elapsed ?? TimeSpan.Zero;

            var samples = new List<double>(_currentSamples);
            return CalculateReport(ActiveSessionName, duration, samples);
        }
    }

    public static BenchmarkReport CalculateReport(string sessionName, TimeSpan duration, List<double> frametimes)
    {
        var report = new BenchmarkReport
        {
            SessionName = sessionName,
            Duration = duration,
            TotalFrames = frametimes.Count,
            FrametimesMs = frametimes
        };

        if (frametimes.Count == 0)
        {
            return report;
        }

        double avgMs = frametimes.Average();
        report.AverageFrametimeMs = Math.Round(avgMs, 2);
        report.AverageFps = avgMs > 0 ? Math.Round(1000.0 / avgMs, 1) : 0;
        report.MinFrametimeMs = Math.Round(frametimes.Min(), 2);
        report.MaxFrametimeMs = Math.Round(frametimes.Max(), 2);

        // Sort descending to find worst frames
        var sortedDesc = frametimes.OrderByDescending(ms => ms).ToList();

        // 1% Low: Average of worst 1% of frames
        int onePercentCount = Math.Max(1, (int)Math.Ceiling(frametimes.Count * 0.01));
        double onePercentAvgMs = sortedDesc.Take(onePercentCount).Average();
        report.OnePercentLowFps = onePercentAvgMs > 0 ? Math.Round(1000.0 / onePercentAvgMs, 1) : 0;

        // 0.1% Low: Average of worst 0.1% of frames
        int pointOnePercentCount = Math.Max(1, (int)Math.Ceiling(frametimes.Count * 0.001));
        double pointOnePercentAvgMs = sortedDesc.Take(pointOnePercentCount).Average();
        report.PointOnePercentLowFps = pointOnePercentAvgMs > 0 ? Math.Round(1000.0 / pointOnePercentAvgMs, 1) : 0;

        // Micro-stutters: frame duration > 2.0x average frametime
        double stutterThreshold = avgMs * 2.0;
        report.StutterCount = frametimes.Count(f => f > stutterThreshold);
        report.StutterPercentage = Math.Round((report.StutterCount * 100.0) / frametimes.Count, 2);

        return report;
    }
}
