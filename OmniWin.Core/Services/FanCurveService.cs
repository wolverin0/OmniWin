using System;
using System.Collections.Generic;
using System.Linq;

namespace OmniWin.Core.Services;

public enum FanSensorSource
{
    Cpu,
    Gpu,
    Storage,
    MaxCpuGpu
}

public class FanCurvePoint
{
    public double TemperatureCelsius { get; set; }
    public double FanSpeedPercent { get; set; }

    public FanCurvePoint(double temp, double speed)
    {
        TemperatureCelsius = temp;
        FanSpeedPercent = Math.Clamp(speed, 0.0, 100.0);
    }
}

public class FanCurveProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FanSensorSource Source { get; set; } = FanSensorSource.Cpu;
    public List<FanCurvePoint> Points { get; set; } = new();
    public double HysteresisCelsius { get; set; } = 3.0;
    public double SmoothingFactor { get; set; } = 0.3; // Exponential moving average weight

    private double _previousTemp = 0.0;
    private double _previousOutput = 30.0;

    public double Evaluate(double currentTemp, bool applySmoothing = true)
    {
        if (Points.Count == 0) return 50.0;

        // Sort points by temperature
        var sorted = Points.OrderBy(p => p.TemperatureCelsius).ToList();

        // Anti-Chatter / Hysteresis Check:
        // If temperature is dropping, but hasn't dropped by more than HysteresisCelsius,
        // hold the previous fan speed to prevent annoying RPM oscillation.
        if (currentTemp < _previousTemp && (_previousTemp - currentTemp) < HysteresisCelsius)
        {
            return _previousOutput;
        }

        double rawOutput;

        if (currentTemp <= sorted.First().TemperatureCelsius)
        {
            rawOutput = sorted.First().FanSpeedPercent;
        }
        else if (currentTemp >= sorted.Last().TemperatureCelsius)
        {
            rawOutput = sorted.Last().FanSpeedPercent;
        }
        else
        {
            // Linear Interpolation between the bounding points
            FanCurvePoint p1 = sorted.First();
            FanCurvePoint p2 = sorted.Last();

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if (currentTemp >= sorted[i].TemperatureCelsius && currentTemp <= sorted[i + 1].TemperatureCelsius)
                {
                    p1 = sorted[i];
                    p2 = sorted[i + 1];
                    break;
                }
            }

            double rangeT = p2.TemperatureCelsius - p1.TemperatureCelsius;
            if (rangeT <= 0.001)
            {
                rawOutput = p2.FanSpeedPercent;
            }
            else
            {
                double fraction = (currentTemp - p1.TemperatureCelsius) / rangeT;
                rawOutput = p1.FanSpeedPercent + fraction * (p2.FanSpeedPercent - p1.FanSpeedPercent);
            }
        }

        // Smoothing: prevent abrupt RPM jumps
        double smoothed = applySmoothing 
            ? ((_previousOutput * (1.0 - SmoothingFactor)) + (rawOutput * SmoothingFactor))
            : rawOutput;
        double finalPercent = Math.Clamp(Math.Round(smoothed, 1), 0.0, 100.0);

        _previousTemp = currentTemp;
        _previousOutput = finalPercent;

        return finalPercent;
    }

    public void ResetState()
    {
        _previousTemp = 0.0;
        _previousOutput = 30.0;
    }
}

public class FanCurveService
{
    public static FanCurveService Instance { get; } = new();

    public List<FanCurveProfile> AvailableProfiles { get; } = new();
    public FanCurveProfile ActiveProfile { get; private set; }

    public FanCurveService()
    {
        InitializeDefaultProfiles();
        ActiveProfile = AvailableProfiles.First(p => p.Id == "balanced");
    }

    private void InitializeDefaultProfiles()
    {
        // 1. Silent / Zero-RPM Mode
        AvailableProfiles.Add(new FanCurveProfile
        {
            Id = "silent_zero_rpm",
            Name = "🤫 Silencio Acústico (Zero-RPM)",
            Description = "Mantiene los ventiladores apagados o al mínimo (<35%) hasta los 60°C. Ideal para oficina y navegación.",
            Source = FanSensorSource.Cpu,
            HysteresisCelsius = 4.0,
            Points = new List<FanCurvePoint>
            {
                new(40.0, 0.0),
                new(55.0, 20.0),
                new(65.0, 35.0),
                new(75.0, 60.0),
                new(85.0, 85.0),
                new(90.0, 100.0)
            }
        });

        // 2. Balanced Profile (Default)
        AvailableProfiles.Add(new FanCurveProfile
        {
            Id = "balanced",
            Name = "⚖️ Equilibrado Diario",
            Description = "Equilibrio acústico con flujo de aire constante para evitar acumulación térmica en el gabinete.",
            Source = FanSensorSource.Cpu,
            HysteresisCelsius = 3.0,
            Points = new List<FanCurvePoint>
            {
                new(35.0, 30.0),
                new(50.0, 40.0),
                new(65.0, 60.0),
                new(75.0, 80.0),
                new(85.0, 100.0)
            }
        });

        // 3. Gamer / Aggressive Profile
        AvailableProfiles.Add(new FanCurveProfile
        {
            Id = "gamer_performance",
            Name = "🎮 Rendimiento Gamer Extremo",
            Description = "Curva agresiva de alta presión estática para mantener CPUs y GPUs en frecuencias boost máximas.",
            Source = FanSensorSource.MaxCpuGpu,
            HysteresisCelsius = 2.5,
            Points = new List<FanCurvePoint>
            {
                new(40.0, 45.0),
                new(55.0, 65.0),
                new(68.0, 85.0),
                new(75.0, 100.0)
            }
        });

        // 4. Mixed Chassis Max(CPU, GPU)
        AvailableProfiles.Add(new FanCurveProfile
        {
            Id = "chassis_mixed_max",
            Name = "🌪️ Curva Mixta Gabinete (Max CPU/GPU)",
            Description = "Toma el valor más alto entre CPU y GPU para que los ventiladores de caja respondan al componente más caliente.",
            Source = FanSensorSource.MaxCpuGpu,
            HysteresisCelsius = 3.5,
            Points = new List<FanCurvePoint>
            {
                new(40.0, 35.0),
                new(55.0, 50.0),
                new(70.0, 75.0),
                new(80.0, 100.0)
            }
        });
    }

    public void SetActiveProfile(string profileId)
    {
        var found = AvailableProfiles.FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (found != null)
        {
            ActiveProfile = found;
            ActiveProfile.ResetState();
        }
    }

    public double CalculateTargetFanSpeed(double cpuTemp, double gpuTemp, bool applySmoothing = true)
    {
        double effectiveTemp = ActiveProfile.Source switch
        {
            FanSensorSource.Cpu => cpuTemp,
            FanSensorSource.Gpu => gpuTemp,
            FanSensorSource.MaxCpuGpu => Math.Max(cpuTemp, gpuTemp),
            _ => cpuTemp
        };

        return ActiveProfile.Evaluate(effectiveTemp, applySmoothing);
    }
}
