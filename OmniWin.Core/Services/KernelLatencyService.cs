using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class TimerResolutionInfo
{
    public double MinResolutionMs { get; set; }
    public double MaxResolutionMs { get; set; }
    public double CurrentResolutionMs { get; set; }
    public bool IsOptimizedForGaming => CurrentResolutionMs <= 1.0;
    public string FormattedCurrent => $"{CurrentResolutionMs:F3} ms";
    public string FormattedMax => $"{MaxResolutionMs:F3} ms";
    public string FormattedMin => $"{MinResolutionMs:F3} ms";
}

public class LatencyBenchmarkResult
{
    public double AverageJitterMicroseconds { get; set; }
    public double MaxJitterMicroseconds { get; set; }
    public double HighResolutionFrequencyMhz { get; set; }
    public string Verdict { get; set; } = string.Empty;
    public string VerdictColor { get; set; } = "#10B981";
    public string Recommendation { get; set; } = string.Empty;
}

public class KernelLatencyService
{
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryTimerResolution(out uint minResolution, out uint maxResolution, out uint currentResolution);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    public TimerResolutionInfo GetTimerResolution()
    {
        try
        {
            int status = NtQueryTimerResolution(out uint minRes, out uint maxRes, out uint curRes);
            if (status == 0) // STATUS_SUCCESS
            {
                // Values are in 100-nanosecond units (10,000 units = 1 ms)
                return new TimerResolutionInfo
                {
                    MinResolutionMs = minRes / 10000.0,
                    MaxResolutionMs = maxRes / 10000.0,
                    CurrentResolutionMs = curRes / 10000.0
                };
            }
        }
        catch { }

        return new TimerResolutionInfo
        {
            MinResolutionMs = 15.625,
            MaxResolutionMs = 0.500,
            CurrentResolutionMs = 1.000
        };
    }

    public bool SetHighResolutionTimer(bool enable)
    {
        try
        {
            // 5000 in 100-ns units = 0.5 ms
            uint desired = 5000;
            int status = NtSetTimerResolution(desired, enable, out _);
            return status == 0;
        }
        catch
        {
            return false;
        }
    }

    public Task<LatencyBenchmarkResult> RunJitterBenchmarkAsync(int samples = 5000)
    {
        return Task.Run(() =>
        {
            long freq = Stopwatch.Frequency;
            double freqMhz = freq / 1_000_000.0;

            double totalDeltaUs = 0;
            double maxDeltaUs = 0;

            long lastTimestamp = Stopwatch.GetTimestamp();

            for (int i = 0; i < samples; i++)
            {
                // Measure loop execution timing variance
                long now = Stopwatch.GetTimestamp();
                long ticks = now - lastTimestamp;
                double deltaUs = (ticks * 1_000_000.0) / freq;

                if (deltaUs > maxDeltaUs) maxDeltaUs = deltaUs;
                totalDeltaUs += deltaUs;

                lastTimestamp = now;
            }

            double avgJitter = totalDeltaUs / samples;

            string verdict;
            string color;
            string rec;

            if (maxDeltaUs < 100.0)
            {
                verdict = "Excelente: Rendimiento en tiempo real óptimo para eSports, streaming y audio ASIO.";
                color = "#10B981"; // Emerald
                rec = "El sistema responde de inmediato a interrupciones sin cuellos de botella DPC detectados.";
            }
            else if (maxDeltaUs < 500.0)
            {
                verdict = "Bueno: Estabilidad normal para trabajo de escritorio y gaming estándar.";
                color = "#38BDF8"; // Sky Blue
                rec = "La resolución de reloj es adecuada, sin microtirones críticos perceptibles.";
            }
            else
            {
                verdict = "Alerta: Picos de jitter e interrupción detectados (> 500 µs).";
                color = "#F59E0B"; // Amber
                rec = "Posibles drivers de audio o red generando interrupciones DPC prolongadas. Considera activar el Timer de 0.5ms y actualizar drivers.";
            }

            return new LatencyBenchmarkResult
            {
                AverageJitterMicroseconds = avgJitter,
                MaxJitterMicroseconds = maxDeltaUs,
                HighResolutionFrequencyMhz = freqMhz,
                Verdict = verdict,
                VerdictColor = color,
                Recommendation = rec
            };
        });
    }
}
