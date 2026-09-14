using System;
using System.Collections.Generic;

namespace OmniWin.Core.Services;

public class ThermalHealthReport
{
    public int HealthScore { get; set; } = 100;
    public string Status { get; set; } = "Salud Térmica Óptima";
    public double EstimatedThermalResistance { get; set; } // °C per Watt
    public bool IsHotIdleDetected { get; set; }
    public bool IsSsdThrottlingRisk { get; set; }
    public bool IsThermalThrottlingLikely { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Recommendations { get; set; } = new();
}

public class ThermalHealthDiagnosticsService
{
    public static ThermalHealthDiagnosticsService Instance { get; } = new();

    public ThermalHealthReport EvaluateSystemHealth(double? cpuTemp, double? cpuLoad, double? cpuPowerWatts, double? gpuTemp, double? maxSsdTemp, double ambientTempCelsius = 24.0)
    {
        var report = new ThermalHealthReport();
        int score = 100;
        var recs = new List<string>();

        double effectiveCpuTemp = cpuTemp ?? 45.0;
        double effectiveCpuLoad = cpuLoad ?? 10.0;
        double effectiveGpuTemp = gpuTemp ?? 40.0;
        double effectiveSsdTemp = maxSsdTemp ?? 42.0;

        // 1. Hot-Idle Analysis (Key indicator of dried thermal paste, mounting issue, or stuck AIO pump)
        if (effectiveCpuLoad < 20.0 && effectiveCpuTemp > 65.0)
        {
            report.IsHotIdleDetected = true;
            score -= 35;
            recs.Add("⚠️ Alerta de Reposo Caliente: El procesador está superando los 65°C casi sin carga de trabajo (<20%).");
            recs.Add("🔧 Muy probable pasta térmica reseca/degradada, disipador mal presionado o bomba de refrigeración líquida bloqueada.");
        }
        else if (effectiveCpuLoad < 20.0 && effectiveCpuTemp > 55.0)
        {
            score -= 15;
            recs.Add("Temperatura en reposo ligeramente alta (>55°C). Revisa la ventilación general del gabinete.");
        }

        // 2. High Load / Thermal Throttling Analysis
        if (effectiveCpuTemp >= 92.0)
        {
            report.IsThermalThrottlingLikely = true;
            score -= 40;
            recs.Add("🚨 Temperatura de CPU en zona de Thermal Throttling (≥92°C). El procesador reduce frecuencias para no quemarse.");
        }
        else if (effectiveCpuTemp >= 83.0)
        {
            score -= 20;
            recs.Add("CPU trabajando a alta temperatura (≥83°C). Conviene optimizar la curva de ventilación o limpiar el polvo de las aletas.");
        }

        // 3. Thermal Resistance Estimation (°C / Watt)
        if (cpuPowerWatts.HasValue && cpuPowerWatts.Value >= 60.0)
        {
            double deltaT = Math.Max(0.0, effectiveCpuTemp - ambientTempCelsius);
            double cPerWatt = deltaT / cpuPowerWatts.Value;
            report.EstimatedThermalResistance = Math.Round(cPerWatt, 3);

            // Inefficient heat transfer under load indicates dry paste or loose mounting
            if (cPerWatt > 0.80)
            {
                score -= 25;
                recs.Add($"Resistencia térmica anómala ({cPerWatt:F2} °C/W). El calor del silicio no se transfiere eficientemente al radiador.");
            }
        }
        else if (cpuPowerWatts.HasValue && cpuPowerWatts.Value > 0)
        {
            double deltaT = Math.Max(0.0, effectiveCpuTemp - ambientTempCelsius);
            report.EstimatedThermalResistance = Math.Round(deltaT / cpuPowerWatts.Value, 3);
        }

        // 4. GPU Thermal Health
        if (effectiveGpuTemp >= 86.0)
        {
            score -= 25;
            recs.Add("GPU en temperatura crítica (≥86°C). Es probable que el Hotspot supere los 100°C. Limpia los ventiladores de la tarjeta gráfica.");
        }
        else if (effectiveGpuTemp >= 78.0)
        {
            score -= 10;
            recs.Add("GPU en temperatura elevada (≥78°C). Aumenta el flujo de entrada de aire frontal del gabinete.");
        }

        // 5. NVMe Storage Thermal Throttling Guard
        if (effectiveSsdTemp >= 70.0)
        {
            report.IsSsdThrottlingRisk = true;
            score -= 20;
            recs.Add("🔥 NVMe SSD en sobrecalentamiento crítico (≥70°C). La controladora NAND está degradando la velocidad de lectura.");
            recs.Add("💡 Instala un disipador pasivo de aluminio o thermal pad en el zócalo M.2.");
        }
        else if (effectiveSsdTemp >= 60.0)
        {
            report.IsSsdThrottlingRisk = true;
            score -= 10;
            recs.Add("NVMe SSD a temperatura moderada-alta (≥60°C). Mantén flujo de aire sobre la placa madre.");
        }

        // Final Score & Status Classification
        report.HealthScore = Math.Clamp(score, 5, 100);

        if (report.HealthScore >= 85)
        {
            report.Status = "Salud Térmica Óptima (Excelente disipación)";
            report.Summary = "El sistema disipa el calor con alta eficiencia. Las temperaturas operativas están en rangos seguros.";
        }
        else if (report.HealthScore >= 65)
        {
            report.Status = "Salud Aceptable (Atención preventiva)";
            report.Summary = "Disipación funcional pero con temperaturas elevadas bajo carga de trabajo.";
        }
        else if (report.HealthScore >= 45)
        {
            report.Status = "Degradación Térmica Detectada";
            report.Summary = "El sistema está experimentando retención de calor excesiva. Se recomienda mantenimiento físico.";
        }
        else
        {
            report.Status = "Alerta Crítica: Fallo de Disipación / Pasta Seca";
            report.Summary = "Riesgo inminente de apagado térmico o estrangulamiento severo de frecuencias.";
        }

        if (recs.Count == 0)
        {
            recs.Add("✔ No se detectaron anomalías térmicas. Todo el hardware opera en parámetros ideales.");
        }

        report.Recommendations = recs;
        return report;
    }
}
