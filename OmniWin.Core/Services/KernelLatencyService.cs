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

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtDelayExecution(bool alertable, ref long delayInterval);

    public Task<LatencyBenchmarkResult> RunJitterBenchmarkAsync(int samples = 50)
    {
        return Task.Run(() =>
        {
            long freq = Stopwatch.Frequency;
            double freqMhz = freq / 1_000_000.0;

            double totalJitterUs = 0;
            double maxJitterUs = 0;

            // Request 1 ms relative delay (-10,000 in 100-nanosecond units)
            long interval = -10000;

            // Warmup
            NtDelayExecution(false, ref interval);

            for (int i = 0; i < samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                NtDelayExecution(false, ref interval);
                long end = Stopwatch.GetTimestamp();

                double actualUs = ((end - start) * 1_000_000.0) / freq;
                // Target is 1,000 µs (1 ms). Jitter is the variance above the target.
                double jitter = actualUs > 1000.0 ? (actualUs - 1000.0) : 0.0;

                if (jitter > maxJitterUs) maxJitterUs = jitter;
                totalJitterUs += jitter;
            }

            double avgJitter = totalJitterUs / samples;

            string verdict;
            string color;
            string rec;

            if (maxJitterUs < 600.0)
            {
                verdict = "Excelente: Temporizador de kernel responsivo, óptimo para eSports y audio en tiempo real.";
                color = "#10B981"; // Emerald
                rec = "El despachador de interrupciones del kernel responde con precisión sub-milisegundo sin contención DPC.";
            }
            else if (maxJitterUs < 1800.0)
            {
                verdict = "Bueno: Estabilidad estándar de Windows para trabajo y gaming.";
                color = "#38BDF8"; // Sky Blue
                rec = "La resolución de reloj es adecuada. Activar el Timer 0.5ms reducirá la latencia residual a la mitad.";
            }
            else
            {
                verdict = "Alerta: Retardo de interrupción o DPC elevado detectado (> 1.8 ms).";
                color = "#F59E0B"; // Amber
                rec = "Un controlador (generalmente GPU, WiFi o audio) o el reloj estándar de 15.6ms retrasan la cola DPC. Considera activar el Timer 0.5ms.";
            }

            return new LatencyBenchmarkResult
            {
                AverageJitterMicroseconds = avgJitter,
                MaxJitterMicroseconds = maxJitterUs,
                HighResolutionFrequencyMhz = freqMhz,
                Verdict = verdict,
                VerdictColor = color,
                Recommendation = rec
            };
        });
    }
}
