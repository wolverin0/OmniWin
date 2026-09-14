using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public enum ExperimentVerdict
{
    Beneficial,
    Neutral,
    Harmful,
    Inconclusive
}

public record ExperimentMetricSummary
{
    public int SampleCount { get; init; }
    public double Mean { get; init; }
    public double StdDev { get; init; }
    public double Median { get; init; }
    public double P95 { get; init; }
    public double P99 { get; init; }      // 1% Low equivalent
    public double P99_9 { get; init; }    // 0.1% Low equivalent
    public double Min { get; init; }
    public double Max { get; init; }
}

public record ExperimentReport
{
    public Guid ExperimentId { get; init; } = Guid.NewGuid();
    public string TweakId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
    public int BaselineDurationSeconds { get; init; }
    public int TreatmentDurationSeconds { get; init; }

    public ExperimentMetricSummary BaselineStats { get; init; } = new();
    public ExperimentMetricSummary TreatmentStats { get; init; } = new();

    public double MeanDeltaPercent { get; init; }
    public double P99DeltaPercent { get; init; }
    public double P99_9DeltaPercent { get; init; }
    public double ConfidenceScore { get; init; } // 0.0 to 1.0 (statistical significance proxy)

    public ExperimentVerdict Verdict { get; init; }
    public string Recommendation { get; init; } = string.Empty;
    public bool AutoReverted { get; init; }
    public string ActionTaken { get; init; } = string.Empty;
}

/// <summary>
/// OmniExperimentEngine: Automated A/B micro-benchmarking engine for Windows optimization.
/// Measures real kernel latency, thread wake jitter, and system stability under rigorous baseline vs treatment phases.
/// Keeps tweaks only when empirically proven beneficial (statistical evidence); auto-reverts neutral or harmful tweaks.
/// </summary>
public class OmniExperimentEngine
{
    private static readonly Lazy<OmniExperimentEngine> _instance = new(() => new OmniExperimentEngine());
    public static OmniExperimentEngine Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly List<ExperimentReport> _history = new();
    private readonly string _historyPath;

    public OmniExperimentEngine(string? customHistoryPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customHistoryPath))
        {
            _historyPath = customHistoryPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "OmniWin", "experiments");
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch { }
            _historyPath = Path.Combine(dir, "history.json");
        }

        LoadHistory();
    }

    public async Task<ExperimentReport> RunExperimentAsync(
        string tweakId,
        int baselineSeconds = 5,
        int treatmentSeconds = 5,
        bool autoRevertIfNotBeneficial = true,
        CancellationToken ct = default)
    {
        if (baselineSeconds < 2) baselineSeconds = 2;
        if (treatmentSeconds < 2) treatmentSeconds = 2;

        var startTime = DateTime.UtcNow;

        // 1. Collect Baseline Samples
        var baselineSamples = await SampleJitterSeriesAsync(TimeSpan.FromSeconds(baselineSeconds), ct);
        var baselineSummary = ComputeSummary(baselineSamples);

        // 2. Apply Tweak via ExpandedTweakService (TransactionService captures pre-state)
        var tweakService = new ExpandedTweakService();
        var applyResult = tweakService.ApplyTweak(tweakId);
        if (!applyResult.Success)
        {
            return new ExperimentReport
            {
                TweakId = tweakId,
                Description = $"Failed to apply tweak: {applyResult.Message}",
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                BaselineStats = baselineSummary,
                Verdict = ExperimentVerdict.Inconclusive,
                Recommendation = "No se pudo aplicar el tweak para el experimento.",
                AutoReverted = false,
                ActionTaken = "Aborted: Apply failed."
            };
        }

        // Brief stabilization window (500ms)
        await Task.Delay(500, ct);

        // 3. Collect Treatment Samples
        var treatmentSamples = await SampleJitterSeriesAsync(TimeSpan.FromSeconds(treatmentSeconds), ct);
        var treatmentSummary = ComputeSummary(treatmentSamples);

        // 4. Calculate Statistical Deltas
        double meanDeltaPct = baselineSummary.Mean > 0
            ? ((treatmentSummary.Mean - baselineSummary.Mean) / baselineSummary.Mean) * 100.0
            : 0.0;

        double p99DeltaPct = baselineSummary.P99 > 0
            ? ((treatmentSummary.P99 - baselineSummary.P99) / baselineSummary.P99) * 100.0
            : 0.0;

        double p99_9DeltaPct = baselineSummary.P99_9 > 0
            ? ((treatmentSummary.P99_9 - baselineSummary.P99_9) / baselineSummary.P99_9) * 100.0
            : 0.0;

        double confidence = CalculateConfidence(baselineSamples, treatmentSamples);

        // 5. Determine Verdict
        // For latency/jitter, negative delta is BETTER (lower jitter)
        ExperimentVerdict verdict;
        string recommendation;

        if (p99DeltaPct <= -3.0 && meanDeltaPct <= -1.5 && confidence >= 0.70)
        {
            verdict = ExperimentVerdict.Beneficial;
            recommendation = $"Mejora verificada empíricamente: reducción del {Math.Abs(p99DeltaPct):F1}% en P99 jitter ({baselineSummary.P99:F1} µs -> {treatmentSummary.P99:F1} µs) con nivel de confianza de {confidence * 100:F0}%. Se recomienda conservar.";
        }
        else if (p99DeltaPct >= 3.0 || meanDeltaPct >= 4.0)
        {
            verdict = ExperimentVerdict.Harmful;
            recommendation = $"Regresión detectada: aumento del {p99DeltaPct:F1}% en P99 jitter ({baselineSummary.P99:F1} µs -> {treatmentSummary.P99:F1} µs). Produce mayor inestabilidad temporal en el kernel.";
        }
        else if (treatmentSamples.Count < 10 || confidence < 0.50)
        {
            verdict = ExperimentVerdict.Inconclusive;
            recommendation = "Muestras insuficientes o varianza ruidosa en el entorno. No se puede certificar una ventaja clara.";
        }
        else
        {
            verdict = ExperimentVerdict.Neutral;
            recommendation = $"Impacto estadísticamente neutral (delta P99: {p99DeltaPct:+0.0;-0.0}%, delta media: {meanDeltaPct:+0.0;-0.0}%). El tweak no ofrece beneficios medibles en este hardware.";
        }

        // 6. Enforce Auto-Revert if requested
        bool reverted = false;
        string actionTaken;

        if (autoRevertIfNotBeneficial && verdict != ExperimentVerdict.Beneficial)
        {
            var rollbackRes = tweakService.RollbackTweak(tweakId);
            reverted = rollbackRes.Success;
            actionTaken = reverted
                ? $"Auto-rollback ejecutado con éxito: el tweak fue revertido a su estado exacto original por veredicto {verdict}."
                : $"Auto-rollback falló al revertir '{tweakId}': {rollbackRes.Message}";
        }
        else if (verdict == ExperimentVerdict.Beneficial)
        {
            actionTaken = $"Tweak conservado: verificado como {verdict} con {confidence * 100:F0}% de confianza.";
        }
        else
        {
            actionTaken = $"Tweak mantenido activo (auto-revert desactivado por el usuario).";
        }

        var report = new ExperimentReport
        {
            TweakId = tweakId,
            Description = $"A/B Experiment: {tweakId}",
            StartedAt = startTime,
            CompletedAt = DateTime.UtcNow,
            BaselineDurationSeconds = baselineSeconds,
            TreatmentDurationSeconds = treatmentSeconds,
            BaselineStats = baselineSummary,
            TreatmentStats = treatmentSummary,
            MeanDeltaPercent = Math.Round(meanDeltaPct, 2),
            P99DeltaPercent = Math.Round(p99DeltaPct, 2),
            P99_9DeltaPercent = Math.Round(p99_9DeltaPct, 2),
            ConfidenceScore = Math.Round(confidence, 2),
            Verdict = verdict,
            Recommendation = recommendation,
            AutoReverted = reverted,
            ActionTaken = actionTaken
        };

        lock (_lock)
        {
            _history.Add(report);
            SaveHistory();
        }

        return report;
    }

    public List<ExperimentReport> GetHistory()
    {
        lock (_lock)
        {
            return _history.OrderByDescending(r => r.StartedAt).ToList();
        }
    }

    private static async Task<List<double>> SampleJitterSeriesAsync(TimeSpan duration, CancellationToken ct)
    {
        var samples = new List<double>();
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < duration && !ct.IsCancellationRequested)
        {
            try
            {
                // Real kernel delay jitter measurement (NtDelayExecution overshoot in microseconds)
                double jitter = KernelLatencyService.Instance.MeasureTimerJitterUs();
                if (jitter >= 0)
                {
                    samples.Add(jitter);
                }
            }
            catch { }

            await Task.Delay(10, ct).ConfigureAwait(false);
        }

        return samples;
    }

    public static ExperimentMetricSummary ComputeSummary(IReadOnlyList<double> samples)
    {
        if (samples == null || samples.Count == 0)
        {
            return new ExperimentMetricSummary();
        }

        var sorted = samples.OrderBy(x => x).ToList();
        double count = sorted.Count;
        double mean = sorted.Average();
        double variance = sorted.Select(x => Math.Pow(x - mean, 2)).Average();
        double stdDev = Math.Sqrt(variance);

        return new ExperimentMetricSummary
        {
            SampleCount = sorted.Count,
            Mean = Math.Round(mean, 2),
            StdDev = Math.Round(stdDev, 2),
            Min = Math.Round(sorted.First(), 2),
            Max = Math.Round(sorted.Last(), 2),
            Median = Math.Round(GetPercentile(sorted, 0.50), 2),
            P95 = Math.Round(GetPercentile(sorted, 0.95), 2),
            P99 = Math.Round(GetPercentile(sorted, 0.99), 2),
            P99_9 = Math.Round(GetPercentile(sorted, 0.999), 2)
        };
    }

    private static double GetPercentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        int idx = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        if (idx < 0) idx = 0;
        if (idx >= sorted.Count) idx = sorted.Count - 1;
        return sorted[idx];
    }

    private static double CalculateConfidence(IReadOnlyList<double> baseline, IReadOnlyList<double> treatment)
    {
        if (baseline.Count < 5 || treatment.Count < 5) return 0.2;

        double meanB = baseline.Average();
        double meanT = treatment.Average();
        double varB = baseline.Select(x => Math.Pow(x - meanB, 2)).Average();
        double varT = treatment.Select(x => Math.Pow(x - meanT, 2)).Average();

        double seDiff = Math.Sqrt((varB / baseline.Count) + (varT / treatment.Count));
        if (seDiff <= 0.0001) return 0.95;

        double tStat = Math.Abs(meanB - meanT) / seDiff;

        // Approximate confidence based on t-statistic
        if (tStat >= 2.58) return 0.99; // p < 0.01
        if (tStat >= 1.96) return 0.95; // p < 0.05
        if (tStat >= 1.64) return 0.90; // p < 0.10
        if (tStat >= 1.28) return 0.80;
        if (tStat >= 1.00) return 0.68;
        return Math.Clamp(tStat * 0.6, 0.1, 0.65);
    }

    private void LoadHistory()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_historyPath))
                {
                    string json = File.ReadAllText(_historyPath);
                    var list = JsonSerializer.Deserialize<List<ExperimentReport>>(json);
                    if (list != null)
                    {
                        _history.Clear();
                        _history.AddRange(list);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OmniExperimentEngine] Error loading history: {ex.Message}");
            }
        }
    }

    private void SaveHistory()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_historyPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_historyPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OmniExperimentEngine] Error saving history: {ex.Message}");
        }
    }
}
